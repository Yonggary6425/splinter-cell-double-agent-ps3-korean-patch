# Splinter Cell Double Agent (PS3, BLUS30032) Korean patch - ISO mode
param([string]$Iso, [string]$Mode)
$ErrorActionPreference = 'Stop'
$here = Split-Path -Parent $MyInvocation.MyCommand.Path
try { [Console]::OutputEncoding = [Text.Encoding]::UTF8 } catch {}

Write-Host '=============================================================='
Write-Host ' 스플린터 셀: 더블 에이전트 (PS3, BLUS30032) 한글패치 - ISO 적용'
Write-Host '=============================================================='
Write-Host ''

$src = Get-Content -Raw -Encoding UTF8 (Join-Path $here 'tools\IsoPatch.cs')
Add-Type -TypeDefinition $src -Language CSharp
[ScdaKor.Patcher]::Log = [Action[string]] { param($s) Write-Host $s }

function Ask([string]$q) { return (Read-Host $q).Trim().Trim('"') }

if (-not $Iso) { $Iso = Ask '패치할 원본 ISO 파일을 이 창에 끌어다 놓고 Enter' }
$Iso = $Iso.Trim().Trim('"')
if (-not (Test-Path -LiteralPath $Iso)) { Write-Host "파일을 찾을 수 없습니다: $Iso"; exit 1 }
$Iso = (Resolve-Path -LiteralPath $Iso).Path

$inner = 'PS3_GAME/USRDIR/offline/system/menu_int.lin'
try { $ok = [ScdaKor.Patcher]::IsoHasFile($Iso, $inner) } catch { $ok = $false }
if (-not $ok) {
  Write-Host '이 ISO에서 게임 파일을 찾지 못했습니다.'
  Write-Host '- 스플린터 셀: 더블 에이전트 북미판(BLUS30032) ISO인지 확인하세요.'
  Write-Host '- 암호화된 ISO는 쓸 수 없습니다. 복호화(decrypted)된 ISO가 필요합니다.'
  exit 1
}

# decrypted original executable (needed for the subtitle code patch)
$elf = $null
foreach ($d in @($here, (Split-Path -Parent $Iso))) {
  $c = Join-Path $d 'sc4_offline.elf'
  if (Test-Path -LiteralPath $c) { $elf = $c; break }
}
if ($elf) {
  $h = (Get-FileHash -Algorithm SHA256 -LiteralPath $elf).Hash
  if ($h -ne [ScdaKor.Patcher]::ElfSha256) { Write-Host "sc4_offline.elf 가 원본과 다릅니다(이미 패치된 파일이거나 다른 버전): $elf"; $elf = $null }
}
if (-not $elf) {
  $self = Join-Path $here 'sc4_offline.self'
  [ScdaKor.Patcher]::ExtractFile($Iso, 'PS3_GAME/USRDIR/offline/sc4_offline.self', $self)
  Write-Host ''
  Write-Host '[준비 필요] 자막 기능이 들어간 실행 파일을 만들려면 원본 실행 파일의 복호화본이 필요합니다.'
  Write-Host "ISO에서 꺼낸 실행 파일을 이 폴더에 저장했습니다: $self"
  Write-Host ''
  Write-Host ' 1. RPCS3 > 유틸리티(Utilities) > PS3 바이너리 복호화(Decrypt PS3 Binaries)'
  Write-Host '    에서 위 sc4_offline.self 를 고릅니다.'
  Write-Host ' 2. 같은 폴더에 sc4_offline.elf 가 생기면 이 패처를 다시 실행하세요.'
  Write-Host ''
  $a = Ask '실행 파일 패치 없이 계속하려면 Y (대사 자막 없이 텍스트만 한글화), 그만두려면 Enter'
  if ($a -ne 'Y' -and $a -ne 'y') { exit 0 }
}

if (-not $Mode) {
  Write-Host ''
  Write-Host '적용 방식을 고르세요.'
  Write-Host ' [1] 새 ISO로 저장 (권장) - 원본은 그대로 두고 "... (KOR).iso" 를 새로 만듭니다.'
  Write-Host ' [2] 원본 ISO에 바로 적용 - 빠르지만 원본이 바뀝니다.'
  $Mode = Ask '번호 입력'
}
$target = $Iso
if ($Mode -ne '2') {
  $target = Join-Path (Split-Path -Parent $Iso) ([IO.Path]::GetFileNameWithoutExtension($Iso) + ' (KOR).iso')
  if (Test-Path -LiteralPath $target) {
    $a = Ask "이미 있는 파일을 덮어씁니다: $target  계속하려면 Y"
    if ($a -ne 'Y' -and $a -ne 'y') { exit 0 }
  }
  $len = (Get-Item -LiteralPath $Iso).Length
  $drive = [IO.Path]::GetPathRoot($target)
  try {
    $free = (New-Object IO.DriveInfo($drive)).AvailableFreeSpace
    if ($free -lt $len + 200MB) { Write-Host ("디스크 공간이 부족합니다. 필요: {0:N1} GB, 남은 공간: {1:N1} GB" -f (($len + 200MB) / 1GB), ($free / 1GB)); exit 1 }
  } catch {}
  Write-Host ''
  Write-Host 'ISO 복사 중...'
  $in = [IO.File]::OpenRead($Iso); $out = [IO.File]::Create($target)
  $buf = New-Object byte[] (8MB); $done = 0L; $lastPct = -1
  while (($r = $in.Read($buf, 0, $buf.Length)) -gt 0) {
    $out.Write($buf, 0, $r); $done += $r
    $pct = [int](100 * $done / $len)
    if ($pct -ne $lastPct) { Write-Progress -Activity 'ISO 복사' -Status "$pct%" -PercentComplete $pct; $lastPct = $pct }
  }
  $in.Close(); $out.Close()
  Write-Progress -Activity 'ISO 복사' -Completed
}

Write-Host ''
Write-Host "패치 적용 중: $target"
try {
  $skipped = [ScdaKor.Patcher]::PatchIso($target, $here, $elf)
} catch {
  Write-Host ''
  Write-Host "오류: $($_.Exception.InnerException.Message) $($_.Exception.Message)"
  if ($target -ne $Iso) { Remove-Item -LiteralPath $target -ErrorAction SilentlyContinue; Write-Host '만들던 ISO를 지웠습니다.' }
  exit 1
}
Write-Host ''
Write-Host '완료!'
Write-Host "결과: $target"
Write-Host ''
Write-Host '※ 콘솔(또는 RPCS3)의 시스템 언어는 영어(English)로 두세요.'
Write-Host '※ 실행 파일이 fake-signed(fself)라서 실기에서는 CFW/HEN 환경이 필요합니다.'
