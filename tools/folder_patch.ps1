# Splinter Cell Double Agent (PS3, BLUS30032) Korean patch applier
param([string]$UsrDir)
$ErrorActionPreference = 'Stop'
$here = Split-Path -Parent $MyInvocation.MyCommand.Path
if (-not $UsrDir) {
  $cand = @(Join-Path $here '..\PS3_GAME\USRDIR'), (Join-Path $here 'PS3_GAME\USRDIR'), (Join-Path $here '..')
  foreach ($c in $cand) { if (Test-Path (Join-Path $c 'offline\system\menu_int.lin')) { $UsrDir = (Resolve-Path $c).Path; break } }
}
if (-not $UsrDir) { $UsrDir = Read-Host 'USRDIR 폴더 경로를 입력하세요 (예: E:\game\PS3_GAME\USRDIR)' }
if (-not (Test-Path (Join-Path $UsrDir 'offline\system\menu_int.lin'))) { Write-Host "USRDIR을 찾을 수 없습니다: $UsrDir"; exit 1 }
Write-Host "대상: $UsrDir"

function Read-Gz([string]$path) {
  $fs = [IO.File]::OpenRead($path)
  $gz = New-Object IO.Compression.GZipStream($fs, [IO.Compression.CompressionMode]::Decompress)
  $ms = New-Object IO.MemoryStream
  $gz.CopyTo($ms); $gz.Close(); $fs.Close()
  return ,$ms.ToArray()
}
function U32([byte[]]$b, [int]$o) { return [BitConverter]::ToUInt32($b, $o) }

$data = Join-Path $here 'data'
$kpts = Get-ChildItem -Path $data -Recurse -Filter '*.kpt'
foreach ($k in $kpts) {
  $rel = $k.FullName.Substring($data.Length + 1); $rel = $rel.Substring(0, $rel.Length - 4)
  $target = Join-Path $UsrDir $rel
  $orig = $target + '.orig'
  if (-not (Test-Path $orig)) { Copy-Item $target $orig }
  $p = Read-Gz $k.FullName
  if ([Text.Encoding]::ASCII.GetString($p, 0, 4) -ne 'KPT1') { throw "bad patch $($k.Name)" }
  $osize = U32 $p 4; $n = U32 $p 8
  $oinfo = Get-Item $orig
  if ($oinfo.Length -ne $osize) { Write-Host "  건너뜀(원본 크기 불일치): $rel"; continue }
  Write-Host "패치 중: $rel"
  # inflate original
  $fs = [IO.File]::OpenRead($orig)
  $hdr = New-Object byte[] 4; [void]$fs.Read($hdr, 0, 4)
  $ds = New-Object IO.Compression.DeflateStream($fs, [IO.Compression.CompressionMode]::Decompress)
  $ms = New-Object IO.MemoryStream
  $ds.CopyTo($ms); $ds.Close(); $fs.Close()
  $src = $ms.GetBuffer(); $srcLen = [int]$ms.Length
  $out = New-Object IO.MemoryStream ($srcLen + 4000000)
  $pos = 12; $last = 0
  for ($i = 0; $i -lt $n; $i++) {
    $s = [int](U32 $p $pos); $e = [int](U32 $p ($pos+4)); $l = [int](U32 $p ($pos+8)); $pos += 12
    $out.Write($src, $last, $s - $last)
    $out.Write($p, $pos, $l); $pos += $l
    $last = $e
  }
  $out.Write($src, $last, $srcLen - $last)
  $ms.Dispose()
  $len = [int]$out.Length
  $fo = [IO.File]::Create($target)
  $be = [BitConverter]::GetBytes([uint32]$len); [Array]::Reverse($be); $fo.Write($be, 0, 4)
  $dz = New-Object IO.Compression.DeflateStream($fo, [IO.Compression.CompressionLevel]::Optimal)
  $dz.Write($out.GetBuffer(), 0, $len); $dz.Close(); $fo.Close()
  $out.Dispose()
  [GC]::Collect()
}
# executable patch (diff against the decrypted original ELF)
$kpes = Get-ChildItem -Path $data -Recurse -Filter '*.kpe'
foreach ($k in $kpes) {
  $rel = $k.FullName.Substring($data.Length + 1); $rel = $rel.Substring(0, $rel.Length - 4)
  $target = Join-Path $UsrDir $rel
  $elf = [IO.Path]::ChangeExtension($target, '.elf')
  if (-not (Test-Path $elf)) {
    Write-Host "  [실행 파일] $([IO.Path]::GetFileName($elf)) 이(가) 없어 실행 파일 패치를 건너뜁니다."
    Write-Host "  RPCS3 > 유틸리티(Utilities) > PS3 바이너리 복호화(Decrypt PS3 Binaries) 에서"
    Write-Host "  $target 을(를) 선택해 같은 폴더에 .elf 를 만든 뒤 다시 실행하세요."
    continue
  }
  $hash = (Get-FileHash -Algorithm SHA256 $elf).Hash
  if ($hash -ne '04B547DA62BBF8DFFE43A34E72606C505F26F128EEA038E25840BC6A79277211') {
    Write-Host "  [실행 파일] 원본 ELF가 예상과 달라 건너뜁니다: $elf"; continue
  }
  Write-Host "패치 중: $rel"
  $p = [IO.File]::ReadAllBytes($k.FullName)
  if ([Text.Encoding]::ASCII.GetString($p, 0, 4) -ne 'KPE1') { throw "bad patch $($k.Name)" }
  $osize = [int](U32 $p 4); $n = [int](U32 $p 8); $hl = [int](U32 $p 12)
  $src = [IO.File]::ReadAllBytes($elf)
  if ($src.Length -ne $osize) { throw "size mismatch $elf" }
  $pos = 16 + $hl
  for ($i = 0; $i -lt $n; $i++) {
    $s = [int](U32 $p $pos); $e = [int](U32 $p ($pos+4)); $l = [int](U32 $p ($pos+8)); $pos += 12
    [Array]::Copy($p, $pos, $src, $s, $l); $pos += $l
  }
  if ((Test-Path $target) -and -not (Test-Path ($target + '.orig'))) { Copy-Item $target ($target + '.orig') }
  $fo = [IO.File]::Create($target)
  $fo.Write($p, 16, $hl); $fo.Write($src, 0, $src.Length); $fo.Close()
}
# plain files
$files = Join-Path $here 'files'
if (Test-Path $files) {
  Get-ChildItem -Path $files -Recurse -File | ForEach-Object {
    $rel = $_.FullName.Substring($files.Length + 1)
    $target = Join-Path $UsrDir $rel
    if ((Test-Path $target) -and -not (Test-Path ($target + '.orig'))) { Copy-Item $target ($target + '.orig') }
    New-Item -ItemType Directory -Force -Path (Split-Path $target) | Out-Null
    Copy-Item $_.FullName $target -Force
    Write-Host "복사: $rel"
  }
}
Write-Host '완료되었습니다.'
