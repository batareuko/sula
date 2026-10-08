<#
.SYNOPSIS
  1 HP · Aion 2: діагностика пінгу і втрат пакетів.

.DESCRIPTION
  Скрипт нічого не змінює в грі й не читає її пам'ять. Він дивиться лише на мережу Windows:
    1. знаходить з'єднання процесу гри (Aion 2) і сервер, до якого вона підключена;
    2. перевіряє адаптер: кабель чи Wi-Fi, сигнал, помилки та відкинуті пакети;
    3. будує маршрут до сервера і одночасно пінгує роутер, кожен вузол маршруту, 1.1.1.1, 8.8.8.8 і сервер;
    4. з правами адміністратора — рахує повторні передачі (ретрансляції) саме на TCP-з'єднанні гри;
    5. за бажанням (-LoadTest) перевіряє, чи росте пінг під навантаженням (bufferbloat);
    6. пише висновок, ДЕ губляться пакети: вдома (Wi-Fi/роутер), у провайдера чи на маршруті до сервера.
  Звіт зберігається на Робочий стіл, його можна надіслати в Discord 1 HP.

  -ApplyTweaks (адміністратор) вмикає безпечні налаштування для онлайн-ігор і зберігає старі значення;
  -Revert повертає все як було.

.EXAMPLE
  Права кнопка на файлі → «Виконати за допомогою PowerShell» (гра має бути запущена, персонаж у світі).

.EXAMPLE
  powershell -NoProfile -ExecutionPolicy Bypass -File .\aion2-netcheck.ps1 -Seconds 120 -LoadTest
#>
[CmdletBinding()]
param(
  [ValidateRange(20, 600)][int]$Seconds = 60,   # тривалість основного тесту
  [string]$Target = '',                          # IP сервера вручну (якщо гру не знайдено)
  [string]$ProcessName = 'aion',                 # частина назви процесу гри
  [switch]$LoadTest,                             # перевірка пінгу під навантаженням
  [switch]$ApplyTweaks,                          # увімкнути безпечні налаштування (адмін)
  [switch]$Revert,                               # повернути налаштування
  [switch]$NoElevate,                            # не пропонувати перезапуск від адміністратора
  [string]$OutDir = ''
)

$ErrorActionPreference = 'Stop'
try { [Console]::OutputEncoding = [Text.Encoding]::UTF8 } catch { }
# Windows PowerShell 5.1 інколи не вмикає TLS 1.2 сам (потрібно для тесту з -LoadTest)
try { [Net.ServicePointManager]::SecurityProtocol = [Net.ServicePointManager]::SecurityProtocol -bor 3072 } catch { }

$script:Report = New-Object System.Collections.Generic.List[string]
$script:BackupDir = Join-Path $(if ($env:LOCALAPPDATA) { $env:LOCALAPPDATA } else { [IO.Path]::GetTempPath() }) '1HP-netcheck'

function Say([string]$Text, [string]$Color = 'Gray') {
  Write-Host $Text -ForegroundColor $Color
  $script:Report.Add($Text)
}
function Head([string]$Text) { Say '' ; Say ('== ' + $Text + ' ==') 'Cyan' }
function Pct([double]$x) { return ('{0:0.#}%' -f $x) }

function Test-Admin {
  try {
    $id = [Security.Principal.WindowsIdentity]::GetCurrent()
    return (New-Object Security.Principal.WindowsPrincipal($id)).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
  } catch { return $false }
}

# ---------------------------------------------------------------- розбір і висновки (чиста логіка, без Windows API)

<# Рядки tracert → список вузлів { Hop; Ip } (вузли, що не відповіли, пропускаються). Мова Windows не важлива. #>
function ConvertFrom-Tracert([string[]]$Lines) {
  $hops = @()
  foreach ($l in $Lines) {
    if ($l -match '^\s*(\d{1,2})\s+(.*)$') {
      $n = [int]$Matches[1]; $rest = $Matches[2]
      $ips = [regex]::Matches($rest, '\b(\d{1,3}(?:\.\d{1,3}){3})\b')
      if ($ips.Count -gt 0) { $hops += [pscustomobject]@{ Hop = $n; Ip = $ips[$ips.Count - 1].Value } }
    }
  }
  return $hops
}

<# Статистика серії пінгів #>
function Get-PingStats([object[]]$Rtts, [int]$Sent) {
  $ok = @($Rtts | Where-Object { $_ -ne $null })
  $lost = $Sent - $ok.Count
  $o = [ordered]@{ Sent = $Sent; Lost = $lost; LossPct = 0.0; Avg = $null; Min = $null; Max = $null; Jitter = $null; Spikes = 0 }
  if ($Sent -gt 0) { $o.LossPct = [math]::Round(100.0 * $lost / $Sent, 1) }
  if ($ok.Count -gt 0) {
    $o.Avg = [math]::Round(($ok | Measure-Object -Average).Average, 1)
    $o.Min = ($ok | Measure-Object -Minimum).Minimum
    $o.Max = ($ok | Measure-Object -Maximum).Maximum
    if ($ok.Count -gt 1) {
      $d = 0.0
      for ($i = 1; $i -lt $ok.Count; $i++) { $d += [math]::Abs($ok[$i] - $ok[$i - 1]) }
      $o.Jitter = [math]::Round($d / ($ok.Count - 1), 1)
    }
    $o.Spikes = @($ok | Where-Object { $_ -gt ($o.Min + 100) }).Count
  }
  return [pscustomobject]$o
}

<#
  Де починаються втрати. $Path — вузли по порядку від роутера до цілі: { Name; Kind (lan|isp|net|dest); LossPct; Responded }.
  Як pathping: втрата на проміжному вузлі, яка НЕ тягнеться далі, — це обмеження ICMP на роутері, не реальна втрата.
  Реальна втрата видна на цілі й на всіх вузлах після місця, де вона почалась.
#>
function Get-LossOrigin([object[]]$Path) {
  $resp = @($Path | Where-Object { $_.Responded })
  if ($resp.Count -eq 0) { return $null }
  $final = $resp[$resp.Count - 1]
  if ($final.LossPct -lt 1) { return [pscustomobject]@{ Origin = $null; FinalLoss = $final.LossPct; Final = $final.Name } }
  $origin = $final
  for ($i = $resp.Count - 1; $i -ge 0; $i--) {
    $tailMin = ($resp[$i..($resp.Count - 1)] | Measure-Object -Property LossPct -Minimum).Minimum
    if ($tailMin -ge [math]::Max(0.7, $final.LossPct * 0.5)) { $origin = $resp[$i] } else { break }
  }
  return [pscustomobject]@{ Origin = $origin; FinalLoss = $final.LossPct; Final = $final.Name }
}

<# Домашня приватна адреса (не CGNAT провайдера 100.64/10) #>
function Test-HomeIp([string]$Ip) { return $Ip -match '^(10\.|192\.168\.|172\.(1[6-9]|2\d|3[01])\.)' }

<#
  Додатковий пристрій-шлюз між ПК і роутером (Raspberry Pi з Pi-hole як шлюз, другий роутер, mesh-точка в режимі роутера):
  два перші вузли маршруту — обидва домашні, і другий схожий на домашній роутер (192.168.x). Провайдерські 10.x другим
  вузлом не рахуються: у багатьох провайдерів внутрішня мережа саме така.
#>
function Get-ExtraGateway([object[]]$Hops) {
  if ($Hops.Count -lt 2) { return $null }
  $a = $Hops[0].Ip; $b = $Hops[1].Ip
  if ((Test-HomeIp $a) -and $b -match '^192\.168\.') { return ($a + ' → ' + $b) }
  return $null
}

<# Висновки: масив { Level (ok|warn|bad); Text } #>
function Get-Verdict($d) {
  $v = New-Object System.Collections.Generic.List[object]
  function Add([string]$lvl, [string]$t) { $v.Add([pscustomobject]@{ Level = $lvl; Text = $t }) }

  $o = $d.Origin
  $downstreamLoss = (-not $o) -or $o.FinalLoss -ge 1
  if ($d.Gateway -and $d.Gateway.LossPct -ge 1 -and -not $downstreamLoss) {
    Add 'warn' ('Роутер іноді не відповідає на ping (' + (Pct $d.Gateway.LossPct) + '), але далі маршрут без втрат: роутер просто навантажений або економить відповіді. Не критично, але його перезавантаження не завадить.')
  }
  if ($d.Gateway -and $d.Gateway.LossPct -ge 1 -and $downstreamLoss) {
    if ($d.IsWifi) { Add 'bad' ('Пакети губляться вже між ПК і роутером (' + (Pct $d.Gateway.LossPct) + '), і це Wi-Fi. Найчастіша причина. Підключіть ПК кабелем або перейдіть на 5 ГГц ближче до роутера; приберіть роутер з-за стін і металу.') }
    else { Add 'bad' ('Пакети губляться між ПК і роутером (' + (Pct $d.Gateway.LossPct) + ') навіть по кабелю: перевірте або замініть кабель і порт, перезавантажте роутер, оновіть драйвер мережевої карти.') }
  }
  if ($d.Gateway -and $d.Gateway.Max -ne $null -and $d.Gateway.Max -gt 50 -and $d.IsWifi) {
    Add 'warn' ('Пінг до роутера стрибає до ' + $d.Gateway.Max + ' мс: перешкоди Wi-Fi (сусідні мережі, мікрохвильовка, Bluetooth). Кабель прибере ці стрибки.')
  }
  if ($d.IsWifi -and $d.WifiSignal -ne $null -and $d.WifiSignal -lt 60) {
    Add 'warn' ('Слабкий сигнал Wi-Fi: ' + $d.WifiSignal + '%. Для ігор бажано від 70%.')
  }
  if ($d.AdapterErrors -gt 0) {
    Add 'bad' ('Мережева карта рахує помилки або відкинуті пакети (' + $d.AdapterErrors + ' за тест): драйвер, кабель або енергозбереження карти. Оновіть драйвер з сайту виробника материнської плати чи карти.')
  }

  if ($o -and $o.Origin) {
    switch ($o.Origin.Kind) {
      'lan'  { } # уже описано вище
      'isp'  { Add 'bad' ('Втрати починаються у вашого провайдера (вузол ' + $o.Origin.Name + ') і тягнуться до кінця маршруту (' + (Pct $o.FinalLoss) + '). Звіт можна показати провайдеру: це їхня ділянка.') }
      'net'  { Add 'bad' ('Втрати починаються далеко за провайдером, на магістральному маршруті (вузол ' + $o.Origin.Name + ', до кінця ' + (Pct $o.FinalLoss) + '). Сам ПК і домашня мережа тут ні до чого. Допомогти може сервіс маршрутизації для ігор: він веде трафік іншим шляхом.') }
      'dest' { Add 'warn' ('Втрати лише на самій цілі (' + $o.Origin.Name + ', ' + (Pct $o.FinalLoss) + '): або перевантажений сервер, або він обмежує відповіді на ping. Дивіться ретрансляції з''єднання гри нижче.') }
    }
  } elseif ($o -and $o.FinalLoss -lt 1 -and -not ($d.Gateway -and $d.Gateway.LossPct -ge 1)) {
    Add 'ok' ('На маршруті до ' + $o.Final + ' втрат немає (' + (Pct $o.FinalLoss) + ').')
  }

  if ($d.GameRetransPct -ne $null) {
    if ($d.GameRetransPct -ge 2) { Add 'bad' ('З''єднання гри повторно надсилає ' + (Pct $d.GameRetransPct) + ' пакетів: це і є «лаги» в бою (скіли із запізненням, ривки).') }
    elseif ($d.GameRetransPct -ge 0.5) { Add 'warn' ('З''єднання гри повторює ' + (Pct $d.GameRetransPct) + ' пакетів: невеликі втрати, у бою можливі мікроривки.') }
    else { Add 'ok' ('З''єднання гри майже без повторів (' + (Pct $d.GameRetransPct) + ').') }
  }
  if ($d.GameRetransPct -eq $null -and $d.SysRetransPct -ne $null -and $d.SysRetransPct -ge 2) {
    Add 'warn' ('Повторні TCP-передачі всього ПК: ' + (Pct $d.SysRetransPct) + '. Це ознака втрат; точніше покаже запуск від адміністратора (статистика саме з''єднання гри).')
  }
  if ($d.PowerSave -and $d.PowerSave.Count -gt 0) {
    Add 'warn' ('Увімкнено енергозбереження мережевої карти (' + ($d.PowerSave -join ', ') + '): воно дає мікророзриви. Вимкне запуск з -ApplyTweaks (від адміністратора).')
  }
  if ($d.GameRtt -ne $null -and $d.GameRtt -ge 180) {
    Add 'warn' ('Пінг до сервера гри ~' + $d.GameRtt + ' мс: сервер далеко (наприклад, азійський сервер з Європи). Так буде завжди через відстань; довгі міжнародні маршрути частіше гублять пакети. Якщо грати на сервері свого регіону, пінг впаде в рази.')
  }
  if ($d.BloatMs -ne $null -and $d.BloatMs -ge 80) {
    Add 'bad' ('Під навантаженням пінг росте на ' + $d.BloatMs + ' мс (bufferbloat): коли хтось удома качає чи стрімить, гра лагає. Увімкніть у роутері QoS / SQM (Smart Queue) або обмежте швидкість завантажень.')
  }
  if ($d.Accel) {
    if ($d.Accel.Running.Count -gt 0) {
      Add 'warn' ('Запущено прискорювач / VPN: ' + ($d.Accel.Running -join ', ') + '. Гра може йти через нього, тож пінг і втрати залежать від його сервера. Для чесної перевірки закрийте його і запустіть тест знову.')
    } elseif ($d.Accel.Services.Count -gt 0 -or $d.Accel.Adapters.Count -gt 0) {
      Add 'warn' ('Знайдено залишки прискорювача / VPN: ' + (@($d.Accel.Services) + @($d.Accel.Adapters) -join ', ') + '. Якщо ним більше не користуєтесь (наприклад, закінчилась підписка ExitLag), видаліть програму повністю (Параметри → Програми) і перезавантажте ПК: її мережевий драйвер може й далі перехоплювати трафік і губити пакети.')
    }
  }
  if ($d.GameViaLoopback) {
    Add 'warn' 'Гра підключена до 127.0.0.1, тобто через локальний проксі прискорювача пінгу. Якщо прискорювач уже не потрібен — вийдіть з нього або видаліть, і гра піде напряму.'
  }
  if ($d.ExtraGateway) {
    Add 'warn' ('Між ПК та інтернетом два домашні пристрої (' + $d.ExtraGateway + '): трафік іде через окремий пристрій (наприклад, Raspberry Pi з Pi-hole) або через два роутери (модем провайдера + свій роутер), а вже потім в інтернет. Кожен зайвий пристрій — ще одне місце, де губляться пакети. Pi-hole краще лишити лише DNS-сервером, а шлюзом на ПК зробити роутер; модем провайдера — перевести в режим моста (bridge).')
  }
  if ($d.Hogs -and $d.Hogs.Count -gt 0) {
    Add 'warn' ('Запущені програми, що можуть забирати канал: ' + ($d.Hogs -join ', ') + '. Під час рейдів призупиніть завантаження й синхронізацію.')
  }
  if ($v.Count -eq 0) { Add 'ok' 'Явних проблем не знайдено. Якщо лаги бувають у певний час — запустіть тест саме тоді (вечір, рейд).' }
  return $v
}

# ---------------------------------------------------------------- вимірювання (Windows)

function Find-GameConnections([string]$NamePart) {
  $procs = @(Get-Process | Where-Object { $_.ProcessName -match [regex]::Escape($NamePart) -and $_.Id -ne $PID })
  if ($procs.Count -eq 0) { return $null }
  $all = @(Get-NetTCPConnection -State Established -ErrorAction SilentlyContinue | Where-Object { $procs.Id -contains $_.OwningProcess })
  $conns = @($all | Where-Object { $_.RemoteAddress -notmatch '^(127\.|::1|0\.0\.0\.0)' })
  $loop = @($all | Where-Object { $_.RemoteAddress -match '^(127\.|::1)' })
  return [pscustomobject]@{ Processes = $procs; Connections = $conns; Loopback = $loop.Count }
}

function Get-DefaultRoute {
  $r = Get-NetRoute -DestinationPrefix '0.0.0.0/0' -ErrorAction SilentlyContinue |
    Sort-Object { $m = Get-NetIPInterface -InterfaceIndex $_.InterfaceIndex -AddressFamily IPv4 -ErrorAction SilentlyContinue; $_.RouteMetric + $(if ($m) { $m.InterfaceMetric } else { 0 }) } |
    Select-Object -First 1
  if (-not $r) { return $null }
  $a = Get-NetAdapter -InterfaceIndex $r.InterfaceIndex -ErrorAction SilentlyContinue
  return [pscustomobject]@{ Gateway = $r.NextHop; IfIndex = $r.InterfaceIndex; Adapter = $a }
}

function Get-WifiInfo {
  try { $out = netsh wlan show interfaces 2>$null } catch { return $null }
  if (-not $out) { return $null }
  $sig = $null; $radio = $null
  foreach ($l in $out) {
    if ($sig -eq $null -and $l -match ':\s*(\d{1,3})\s*%') { $sig = [int]$Matches[1] }
    if ($radio -eq $null -and $l -match '(802\.11\w+)') { $radio = $Matches[1] }
  }
  return [pscustomobject]@{ Signal = $sig; Radio = $radio }
}

function Get-AdapterCounters([string]$Name) {
  try {
    $s = Get-NetAdapterStatistics -Name $Name
    return [pscustomobject]@{
      Err = [int64]$s.ReceivedPacketErrors + [int64]$s.OutboundPacketErrors
      Disc = [int64]$s.ReceivedDiscardedPackets + [int64]$s.OutboundDiscardedPackets
    }
  } catch { return $null }
}

function Get-TcpCounters {
  try {
    $t = Get-CimInstance -ClassName Win32_PerfRawData_Tcpip_TCPv4
    return [pscustomobject]@{ Sent = [int64]$t.SegmentsSentPersec; Retrans = [int64]$t.SegmentsRetransmittedPersec }
  } catch { return $null }
}

<# Одночасний ping кількох цілей: кожні IntervalMs мс по одному запиту на кожну ціль #>
function Invoke-MultiPing([string[]]$Ips, [int]$Seconds, [int]$IntervalMs = 500, [int]$TimeoutMs = 1000) {
  $res = @{}; foreach ($ip in $Ips) { $res[$ip] = New-Object System.Collections.Generic.List[object] }
  $ticks = [math]::Max(1, [int]($Seconds * 1000 / $IntervalMs))
  $pingers = @{}; foreach ($ip in $Ips) { $pingers[$ip] = New-Object System.Net.NetworkInformation.Ping }
  for ($t = 0; $t -lt $ticks; $t++) {
    $start = [DateTime]::UtcNow
    $tasks = @{}
    foreach ($ip in $Ips) {
      try { $tasks[$ip] = $pingers[$ip].SendPingAsync($ip, $TimeoutMs) }
      catch { $tasks[$ip] = $null; $pingers[$ip] = New-Object System.Net.NetworkInformation.Ping } # попередній запит ще висить
    }
    $live = @($tasks.Values | Where-Object { $_ -ne $null })
    if ($live.Count) { try { [void][Threading.Tasks.Task]::WaitAll([Threading.Tasks.Task[]]$live, $TimeoutMs + 500) } catch { } }
    foreach ($ip in $Ips) {
      $tk = $tasks[$ip]; $rtt = $null
      if ($tk -and $tk.IsCompleted -and -not $tk.IsFaulted -and $tk.Result.Status -eq 'Success') { $rtt = [int]$tk.Result.RoundtripTime }
      $res[$ip].Add($rtt)
    }
    if (($t % 10) -eq 0) { Write-Progress -Activity 'Пінг-тест' -Status ('{0} з {1} с' -f [int]($t * $IntervalMs / 1000), $Seconds) -PercentComplete (100 * $t / $ticks) }
    $left = $IntervalMs - ([DateTime]::UtcNow - $start).TotalMilliseconds
    if ($left -gt 0) { Start-Sleep -Milliseconds ([int]$left) }
  }
  Write-Progress -Activity 'Пінг-тест' -Completed
  $out = @{}; foreach ($ip in $Ips) { $out[$ip] = Get-PingStats -Rtts $res[$ip].ToArray() -Sent $ticks }
  return $out
}

<# Детальна статистика TCP-з'єднання гри (Windows ESTATS, лише IPv4, потрібен адміністратор) #>
$EStatsSource = @'
using System;
using System.Runtime.InteropServices;
public static class OneHpEStats {
  [StructLayout(LayoutKind.Sequential)] public struct MIB_TCPROW { public uint State, LocalAddr, LocalPort, RemoteAddr, RemotePort; }
  [StructLayout(LayoutKind.Sequential)] public struct RW { public byte Enable; }
  [StructLayout(LayoutKind.Sequential)] public struct PATH_ROD {
    public uint FastRetran, Timeouts, SubsequentTimeouts, CurTimeoutCount, AbruptTimeouts, PktsRetrans, BytesRetrans, DupAcksIn,
      SacksRcvd, SackBlocksRcvd, CongSignals, PreCongSumCwnd, PreCongSumRtt, PostCongSumRtt, PostCongCountRtt, EcnSignals, EceRcvd,
      SendStall, QuenchRcvd, RetranThresh, SndDupAckEpisodes, SumBytesReordered, NonRecovDa, NonRecovDaEpisodes, AckAfterFr, DsackDups,
      SampleRtt, SmoothedRtt, RttVar, MaxRtt, MinRtt, SumRtt, CountRtt, CurRto, MaxRto, MinRto, CurMss, MaxMss, MinMss, SpuriousRtoDetections;
  }
  [StructLayout(LayoutKind.Sequential)] public struct DATA_ROD {
    public ulong DataBytesOut, DataSegsOut, DataBytesIn, DataSegsIn, SegsOut, SegsIn;
    public uint SoftErrors, SoftErrorReason, SndUna, SndNxt, SndMax;
    public ulong ThruBytesAcked; public uint RcvNxt; public ulong ThruBytesReceived;
  }
  [DllImport("iphlpapi.dll")] static extern uint SetPerTcpConnectionEStats(ref MIB_TCPROW row, int type, ref RW rw, uint ver, uint size, uint offset);
  [DllImport("iphlpapi.dll")] static extern uint GetPerTcpConnectionEStats(ref MIB_TCPROW row, int type, IntPtr rw, uint rwVer, uint rwSize,
    IntPtr ros, uint rosVer, uint rosSize, IntPtr rod, uint rodVer, uint rodSize);
  const int DATA = 1, PATH = 3;
  public static MIB_TCPROW Row(byte[] la, int lp, byte[] ra, int rp) {
    MIB_TCPROW r = new MIB_TCPROW();
    r.State = 5; // ESTABLISHED
    r.LocalAddr = BitConverter.ToUInt32(la, 0); r.RemoteAddr = BitConverter.ToUInt32(ra, 0);
    r.LocalPort = (uint)(((lp & 0xFF) << 8) | ((lp >> 8) & 0xFF)); r.RemotePort = (uint)(((rp & 0xFF) << 8) | ((rp >> 8) & 0xFF));
    return r;
  }
  public static uint Enable(MIB_TCPROW r) {
    RW rw = new RW(); rw.Enable = 1;
    uint a = SetPerTcpConnectionEStats(ref r, DATA, ref rw, 0, (uint)Marshal.SizeOf(typeof(RW)), 0);
    uint b = SetPerTcpConnectionEStats(ref r, PATH, ref rw, 0, (uint)Marshal.SizeOf(typeof(RW)), 0);
    return a != 0 ? a : b;
  }
  static T Read<T>(MIB_TCPROW r, int type) where T : struct {
    int size = Marshal.SizeOf(typeof(T)); IntPtr p = Marshal.AllocHGlobal(size);
    try {
      uint e = GetPerTcpConnectionEStats(ref r, type, IntPtr.Zero, 0, 0, IntPtr.Zero, 0, 0, p, 0, (uint)size);
      if (e != 0) throw new Exception("GetPerTcpConnectionEStats " + e);
      return (T)Marshal.PtrToStructure(p, typeof(T));
    } finally { Marshal.FreeHGlobal(p); }
  }
  public static PATH_ROD Path(MIB_TCPROW r) { return Read<PATH_ROD>(r, PATH); }
  public static DATA_ROD Data(MIB_TCPROW r) { return Read<DATA_ROD>(r, DATA); }
}
'@

function Start-GameEStats($Conn) {
  try {
    if (-not ('OneHpEStats' -as [type])) { Add-Type -TypeDefinition $EStatsSource -Language CSharp }
    $la = [Net.IPAddress]::Parse($Conn.LocalAddress).GetAddressBytes()
    $ra = [Net.IPAddress]::Parse($Conn.RemoteAddress).GetAddressBytes()
    if ($la.Length -ne 4 -or $ra.Length -ne 4) { return $null }
    $row = [OneHpEStats]::Row($la, [int]$Conn.LocalPort, $ra, [int]$Conn.RemotePort)
    $e = [OneHpEStats]::Enable($row)
    if ($e -ne 0) { return $null }
    $p0 = [OneHpEStats]::Path($row); $d0 = [OneHpEStats]::Data($row)
    return [pscustomobject]@{ Row = $row; Retrans0 = $p0.PktsRetrans; Segs0 = $d0.DataSegsOut; Timeouts0 = $p0.Timeouts }
  } catch { return $null }
}

function Stop-GameEStats($S) {
  if (-not $S) { return $null }
  try {
    $p = [OneHpEStats]::Path($S.Row); $d = [OneHpEStats]::Data($S.Row)
    $segs = [double]($d.DataSegsOut - $S.Segs0); $re = [double]($p.PktsRetrans - $S.Retrans0)
    $pct = $null; if ($segs -gt 20) { $pct = [math]::Round(100.0 * $re / $segs, 2) }
    return [pscustomobject]@{ Segs = $segs; Retrans = $re; RetransPct = $pct; Timeouts = $p.Timeouts - $S.Timeouts0
      RttMs = $p.SmoothedRtt; MinRtt = $p.MinRtt; MaxRtt = $p.MaxRtt }
  } catch { return $null }
}

<# Пінг під навантаженням: 10 с завантаження з CDN Cloudflare, паралельно ping 1.1.1.1 #>
function Measure-Bufferbloat {
  $base = (Invoke-MultiPing -Ips @('1.1.1.1') -Seconds 5)['1.1.1.1']
  $wc = New-Object System.Net.WebClient
  $task = $wc.DownloadDataTaskAsync('https://speed.cloudflare.com/__down?bytes=200000000')
  $load = (Invoke-MultiPing -Ips @('1.1.1.1') -Seconds 10)['1.1.1.1']
  try { $wc.CancelAsync() } catch { }
  if ($base.Avg -eq $null -or $load.Avg -eq $null) { return $null }
  return [pscustomobject]@{ Base = $base.Avg; Load = $load.Avg; Delta = [math]::Round($load.Avg - $base.Avg) }
}

# ---------------------------------------------------------------- налаштування (-ApplyTweaks / -Revert)

function Get-TweakTargets($Route) {
  $guid = $Route.Adapter.InterfaceGuid
  return [pscustomobject]@{
    TcpKey = 'HKLM:\SYSTEM\CurrentControlSet\Services\Tcpip\Parameters\Interfaces\' + $guid
    Adapter = $Route.Adapter.Name
  }
}

function Invoke-Tweaks($Route, [switch]$Undo) {
  if (-not (Test-Admin)) { Say 'Налаштування потребують прав адміністратора.' 'Yellow'; return }
  $t = Get-TweakTargets $Route
  $file = Join-Path $script:BackupDir 'backup.json'
  if ($Undo) {
    if (-not (Test-Path $file)) { Say 'Резервної копії немає: нічого повертати.' 'Yellow'; return }
    $b = Get-Content $file -Raw | ConvertFrom-Json
    foreach ($r in $b.Registry) {
      if ($r.Value -eq $null) { Remove-ItemProperty -Path $r.Key -Name $r.Name -ErrorAction SilentlyContinue }
      else { Set-ItemProperty -Path $r.Key -Name $r.Name -Value $r.Value -Type DWord }
    }
    foreach ($a in $b.Adapter) { Set-NetAdapterAdvancedProperty -Name $a.Adapter -RegistryKeyword $a.Keyword -RegistryValue $a.Value -ErrorAction SilentlyContinue }
    Remove-Item $file
    Say 'Налаштування повернуто. Перезавантажте ПК, щоб усе застосувалось.' 'Green'
    return
  }
  New-Item -ItemType Directory -Force -Path $script:BackupDir | Out-Null
  $backup = [ordered]@{ Registry = @(); Adapter = @(); Created = (Get-Date).ToString('s') }
  if (Test-Path $file) { Say 'Налаштування вже застосовано раніше (є резервна копія). Спершу -Revert, якщо треба застосувати знову.' 'Yellow'; return }

  # 1. Без алгоритму Нейгла й затримки підтвердження: дрібні пакети гри йдуть одразу
  foreach ($name in 'TcpAckFrequency', 'TCPNoDelay') {
    $old = (Get-ItemProperty -Path $t.TcpKey -Name $name -ErrorAction SilentlyContinue).$name
    $backup.Registry += [pscustomobject]@{ Key = $t.TcpKey; Name = $name; Value = $old }
    Set-ItemProperty -Path $t.TcpKey -Name $name -Value 1 -Type DWord
  }
  Say 'Вимкнено затримку дрібних TCP-пакетів (TcpAckFrequency=1, TCPNoDelay=1) для активного адаптера.' 'Green'

  # 2. Енергозбереження Ethernet (EEE / Green Ethernet) дає мікророзриви — вимикаємо, якщо карта це підтримує
  foreach ($kw in '*EEE', 'EEELinkAdvertisement', 'EnableGreenEthernet', 'AdvancedEEE', 'PowerSavingMode') {
    $p = Get-NetAdapterAdvancedProperty -Name $t.Adapter -RegistryKeyword $kw -ErrorAction SilentlyContinue
    if ($p -and "$($p.RegistryValue)" -ne '0') {
      $backup.Adapter += [pscustomobject]@{ Adapter = $t.Adapter; Keyword = $kw; Value = "$($p.RegistryValue)" }
      Set-NetAdapterAdvancedProperty -Name $t.Adapter -RegistryKeyword $kw -RegistryValue 0 -ErrorAction SilentlyContinue
      Say ('Вимкнено енергозбереження мережевої карти: ' + $p.DisplayName) 'Green'
    }
  }
  ($backup | ConvertTo-Json -Depth 4) | Set-Content -Path $file -Encoding UTF8
  Say ('Резервна копія: ' + $file + '. Повернути: запустіть скрипт з -Revert.') 'Gray'
  Say 'Перезавантажте ПК, щоб налаштування TCP застосувались.' 'Yellow'
}

# ---------------------------------------------------------------- головний сценарій

function Invoke-NetCheck {
  Say '1 HP · Aion 2 — діагностика пінгу і втрат пакетів' 'White'
  Say ('Дата: ' + (Get-Date).ToString('yyyy-MM-dd HH:mm') + ' · Windows ' + [Environment]::OSVersion.Version) 'DarkGray'
  $admin = Test-Admin

  if (-not $admin -and -not $NoElevate -and $PSCommandPath -and -not $Revert) {
    $ans = Read-Host 'Перезапустити від імені адміністратора? Тоді буде видно втрати саме на з''єднанні гри (Y/n)'
    if ($ans -notmatch '^(n|н)') {
      $argList = @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', ('"' + $PSCommandPath + '"'), '-Seconds', $Seconds, '-NoElevate')
      if ($Target) { $argList += @('-Target', $Target) }
      if ($LoadTest) { $argList += '-LoadTest' }
      if ($ApplyTweaks) { $argList += '-ApplyTweaks' }
      try { Start-Process powershell -Verb RunAs -ArgumentList $argList; return } catch { Say 'Без прав адміністратора: частина перевірок буде пропущена.' 'Yellow' }
    }
  }

  $route = Get-DefaultRoute
  if (-not $route) { Say 'Не знайдено підключення до інтернету (немає маршруту за замовчуванням).' 'Red'; return }

  if ($Revert) { Invoke-Tweaks -Route $route -Undo; return }

  # --- гра
  Head 'Гра'
  $game = Find-GameConnections $ProcessName
  $gameConn = $null
  $gameViaLoopback = $false
  if ($game -and $game.Connections.Count -gt 0) {
    Say ('Процес: ' + (($game.Processes | ForEach-Object { $_.ProcessName + ' (' + $_.Id + ')' }) -join ', '))
    $byRemote = $game.Connections | Group-Object RemoteAddress | Sort-Object Count -Descending
    foreach ($g in $byRemote) { Say ('  з''єднання з ' + $g.Name + ' порт ' + (($g.Group.RemotePort | Select-Object -Unique) -join ',')) }
    # ігровий сервер — найчастіше нестандартний порт (не 443/80, де лаунчер і веб)
    $gameConn = ($game.Connections | Where-Object { $_.RemotePort -ne 443 -and $_.RemotePort -ne 80 } | Select-Object -First 1)
    if (-not $gameConn) { $gameConn = $game.Connections | Select-Object -First 1 }
    if (-not $Target) { $Target = $gameConn.RemoteAddress }
    Say ('Сервер гри: ' + $Target + ':' + $gameConn.RemotePort) 'White'
  } elseif ($game -and $game.Loopback -gt 0) {
    $gameViaLoopback = $true
    Say ('Гра підключена до 127.0.0.1 (' + $game.Loopback + ' з''єднань): це локальний проксі прискорювача пінгу (ExitLag тощо). Сервер гри за ним не видно.') 'Yellow'
  } elseif ($Target) {
    Say ('Гру не знайдено, перевіряю вказаний сервер ' + $Target) 'Yellow'
  } else {
    Say 'Гру не знайдено. Запустіть Aion 2 і зайдіть персонажем у світ, тоді перезапустіть перевірку. Поки перевіряю загальний інтернет.' 'Yellow'
  }

  # --- адаптер
  Head 'Підключення'
  $ad = $route.Adapter
  $isWifi = $false
  if ($ad) {
    $isWifi = ($ad.PhysicalMediaType -match '802\.11|Wireless') -or ($ad.InterfaceDescription -match 'Wi-?Fi|Wireless|WLAN|802\.11')
    Say ('Адаптер: ' + $ad.Name + ' · ' + $ad.InterfaceDescription + ' · ' + $ad.LinkSpeed + ' · ' + $(if ($isWifi) { 'Wi-Fi' } else { 'кабель' }))
  }
  Say ('Роутер (шлюз): ' + $route.Gateway)
  try {
    $dns = @((Get-DnsClientServerAddress -InterfaceIndex $route.IfIndex -AddressFamily IPv4 -ErrorAction Stop).ServerAddresses)
    if ($dns.Count) {
      $dnsNote = $(if (($dns | Where-Object { (Test-HomeIp $_) -and $_ -ne $route.Gateway }).Count) { ' (окремий пристрій, напр. Pi-hole: на пінг у грі DNS не впливає)' } else { '' })
      Say ('DNS: ' + ($dns -join ', ') + $dnsNote)
    }
  } catch { }

  # прискорювачі пінгу / VPN: запущені, або залишки служб і мережевих адаптерів після видалення / кінця підписки
  $accelRx = 'ExitLag|LagoFast|GearUP|NoPing|WTFast|Mudfish|Outfox|Haste|Kovi'
  $safe = { param($sb) try { @(& $sb) } catch { @() } }
  $accel = [pscustomobject]@{
    Running = & $safe { Get-Process -ErrorAction SilentlyContinue | Where-Object { $_.ProcessName -match $accelRx } | Select-Object -ExpandProperty ProcessName -Unique }
    Services = & $safe { Get-Service -ErrorAction SilentlyContinue | Where-Object { $_.Name -match $accelRx -or $_.DisplayName -match $accelRx } | ForEach-Object { 'служба ' + $_.DisplayName + ' (' + $_.Status + ')' } }
    Adapters = & $safe { Get-NetAdapter -IncludeHidden -ErrorAction SilentlyContinue | Where-Object { $_.InterfaceDescription -match ($accelRx + '|TAP-Windows|Wintun') -or $_.Name -match $accelRx } | ForEach-Object { 'адаптер ' + $_.InterfaceDescription + ' (' + $_.Status + ')' } }
  }
  foreach ($x in @($accel.Running | ForEach-Object { 'запущено ' + $_ }) + $accel.Services + $accel.Adapters) { Say ('Прискорювач / VPN: ' + $x) 'Yellow' }
  $wifi = $null
  if ($isWifi) {
    $wifi = Get-WifiInfo
    if ($wifi) { Say ('Wi-Fi: сигнал ' + $wifi.Signal + '% · ' + $wifi.Radio) }
  }
  $powerSave = @()
  if ($ad) {
    foreach ($kw in '*EEE', 'EnableGreenEthernet', 'PowerSavingMode', 'AdvancedEEE', '*InterruptModeration') {
      $p = Get-NetAdapterAdvancedProperty -Name $ad.Name -RegistryKeyword $kw -ErrorAction SilentlyContinue
      if ($p) {
        Say ('  ' + $p.DisplayName + ': ' + $p.DisplayValue) 'DarkGray'
        if ($kw -ne '*InterruptModeration' -and "$($p.RegistryValue)" -ne '0') { $powerSave += $p.DisplayName }
      }
    }
  }

  # --- маршрут
  Head 'Маршрут'
  $traceTo = $(if ($Target) { $Target } else { '1.1.1.1' })
  Say ('Будую маршрут до ' + $traceTo + ' (до 30 с)...') 'DarkGray'
  $hops = @(ConvertFrom-Tracert (tracert -d -h 25 -w 700 $traceTo))
  foreach ($h in $hops) { Say ('  ' + $h.Hop + '. ' + $h.Ip) 'DarkGray' }
  $extraGateway = Get-ExtraGateway $hops
  # PPPoE та деякі VPN мають шлюз 0.0.0.0: тоді «роутером» вважаємо перший вузол маршруту
  if ($route.Gateway -eq '0.0.0.0' -or -not $route.Gateway) {
    if ($hops.Count) { $route.Gateway = $hops[0].Ip; Say ('Шлюз 0.0.0.0 (PPPoE/VPN): перший вузол ' + $route.Gateway + ' вважаю роутером.') 'DarkGray' }
  }

  # --- лічильники до тесту
  $ac0 = $(if ($ad) { Get-AdapterCounters $ad.Name } else { $null })
  $tcp0 = Get-TcpCounters
  $es = $null
  if ($gameConn -and $admin) { $es = Start-GameEStats $gameConn }
  if ($gameConn -and -not $es) { Say 'Статистика з''єднання гри недоступна (потрібен адміністратор або з''єднання IPv6).' 'DarkGray' }

  # --- основний пінг-тест
  Head ('Пінг-тест ' + $Seconds + ' с (грайте як завжди, бажано в бою чи місті)')
  $targets = New-Object System.Collections.Generic.List[string]
  $targets.Add($route.Gateway)
  foreach ($h in $hops) { if (-not $targets.Contains($h.Ip)) { $targets.Add($h.Ip) } }
  foreach ($ip in '1.1.1.1', '8.8.8.8') { if (-not $targets.Contains($ip)) { $targets.Add($ip) } }
  if ($Target -and -not $targets.Contains($Target)) { $targets.Add($Target) }
  $stats = Invoke-MultiPing -Ips $targets.ToArray() -Seconds $Seconds

  $fmt = { param($name, $s) ('{0,-28} втрати {1,6}  пінг {2,6} мс  мін {3,4}  макс {4,5}  джитер {5,5}' -f $name, (Pct $s.LossPct), $s.Avg, $s.Min, $s.Max, $s.Jitter) }
  Say (& $fmt ('Роутер ' + $route.Gateway) $stats[$route.Gateway])
  foreach ($h in $hops) { if ($h.Ip -ne $route.Gateway) { Say (& $fmt ('Вузол ' + $h.Hop + ' ' + $h.Ip) $stats[$h.Ip]) } }
  Say (& $fmt 'Cloudflare 1.1.1.1' $stats['1.1.1.1'])
  Say (& $fmt 'Google 8.8.8.8' $stats['8.8.8.8'])
  $gameStat = $null
  if ($Target) {
    $gameStat = $stats[$Target]
    Say (& $fmt ('Сервер гри ' + $Target) $gameStat) 'White'
    if ($gameStat.Lost -eq $gameStat.Sent) { Say '  Сервер гри не відповідає на ping — це нормально для багатьох ігрових серверів.' 'DarkGray' }
  }

  # --- лічильники після тесту
  $adapterErr = 0
  if ($ac0) { $ac1 = Get-AdapterCounters $ad.Name; if ($ac1) { $adapterErr = ($ac1.Err - $ac0.Err) + ($ac1.Disc - $ac0.Disc) } }
  $tcp1 = Get-TcpCounters
  $sysRetrans = $null
  if ($tcp0 -and $tcp1 -and ($tcp1.Sent - $tcp0.Sent) -gt 100) { $sysRetrans = [math]::Round(100.0 * ($tcp1.Retrans - $tcp0.Retrans) / ($tcp1.Sent - $tcp0.Sent), 2) }
  $ge = Stop-GameEStats $es

  Head 'Лічильники'
  Say ('Помилки/відкинуті пакети мережевої карти за тест: ' + $adapterErr)
  if ($sysRetrans -ne $null) { Say ('Повторні TCP-передачі всього ПК: ' + (Pct $sysRetrans)) }
  if ($ge) {
    Say ('З''єднання гри: надіслано ' + $ge.Segs + ' пакетів, повторно ' + $ge.Retrans + ' (' + $(if ($ge.RetransPct -ne $null) { Pct $ge.RetransPct } else { 'замало даних' }) + '), тайм-аутів ' + $ge.Timeouts) 'White'
    Say ('Пінг з''єднання гри (TCP): зараз ' + $ge.RttMs + ' мс, мін ' + $ge.MinRtt + ', макс ' + $ge.MaxRtt) 'White'
  }

  # --- навантаження
  $bloat = $null
  if ($LoadTest) {
    Head 'Пінг під навантаженням'
    $bloat = Measure-Bufferbloat
    if ($bloat) { Say ('Без навантаження ' + $bloat.Base + ' мс, під час завантаження ' + $bloat.Load + ' мс (+' + $bloat.Delta + ')') }
  }

  # --- фонові програми
  $hogNames = 'OneDrive', 'Dropbox', 'GoogleDriveFS', 'qbittorrent', 'utorrent', 'BitTorrent', 'transmission-qt', 'EpicGamesLauncher', 'Battle.net', 'obs64', 'MEGAsync', 'YandexDisk2', 'steamwebhelper'
  $hogs = @(Get-Process -ErrorAction SilentlyContinue | Where-Object { $hogNames -contains $_.ProcessName } | Select-Object -ExpandProperty ProcessName -Unique)
  $hogs = @($hogs | ForEach-Object { if ($_ -eq 'steamwebhelper') { 'Steam (якщо качає оновлення)' } else { $_ } })

  # --- шлях для пошуку джерела втрат
  $path = @()
  $path += [pscustomobject]@{ Name = 'роутер ' + $route.Gateway; Kind = 'lan'; LossPct = $stats[$route.Gateway].LossPct; Responded = $stats[$route.Gateway].Lost -lt $stats[$route.Gateway].Sent }
  $i = 0
  foreach ($h in $hops) {
    $i++
    if ($h.Ip -eq $route.Gateway) { continue }
    $kind = $(if ($h.Ip -eq $Target) { 'dest' } elseif ($i -le 3) { 'isp' } else { 'net' })
    $s = $stats[$h.Ip]
    $path += [pscustomobject]@{ Name = $h.Ip; Kind = $kind; LossPct = $s.LossPct; Responded = $s.Lost -lt $s.Sent }
  }
  $origin = Get-LossOrigin $path

  $data = [pscustomobject]@{
    Gateway = $stats[$route.Gateway]; IsWifi = $isWifi; WifiSignal = $(if ($wifi) { $wifi.Signal } else { $null })
    AdapterErrors = $adapterErr; Origin = $origin
    GameRetransPct = $(if ($ge) { $ge.RetransPct } else { $null })
    GameRtt = $(if ($ge -and $ge.RttMs) { $ge.RttMs } elseif ($gameStat -and $gameStat.Avg) { $gameStat.Avg } else { $null })
    BloatMs = $(if ($bloat) { $bloat.Delta } else { $null })
    Hogs = $hogs; SysRetransPct = $sysRetrans; PowerSave = $powerSave
    Accel = $accel; GameViaLoopback = $gameViaLoopback; ExtraGateway = $extraGateway
  }

  Head 'Висновок'
  foreach ($x in (Get-Verdict $data)) {
    $c = switch ($x.Level) { 'bad' { 'Red' } 'warn' { 'Yellow' } default { 'Green' } }
    $mark = switch ($x.Level) { 'bad' { '[!]' } 'warn' { '[~]' } default { '[ok]' } }
    Say ($mark + ' ' + $x.Text) $c
  }

  if ($ApplyTweaks) { Head 'Налаштування'; Invoke-Tweaks -Route $route }
  elseif ($admin) { Say ''; Say 'Порада: запустіть з -ApplyTweaks, щоб вимкнути затримку дрібних TCP-пакетів і енергозбереження мережевої карти (з резервною копією).' 'DarkGray' }

  # --- звіт
  if (-not $OutDir) { $OutDir = [Environment]::GetFolderPath('Desktop') }
  $file = Join-Path $OutDir ('aion2-netcheck-' + (Get-Date).ToString('yyyyMMdd-HHmm') + '.txt')
  try { $script:Report | Set-Content -Path $file -Encoding UTF8; Say ''; Say ('Звіт збережено: ' + $file) 'Cyan' } catch { }
}

if ($MyInvocation.InvocationName -ne '.') {
  try { Invoke-NetCheck } catch { Write-Host ('Помилка: ' + $_.Exception.Message) -ForegroundColor Red }
  if ($Host.Name -eq 'ConsoleHost') { [void](Read-Host 'Натисніть Enter, щоб закрити') }
}
