cosmos build
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

$qemuDir = "$env:LOCALAPPDATA\Cosmos\Tools\qemu"
$disk = ".\disk.img"

# A blank image is partitioned and formatted as FAT32 by the kernel on first boot.
if (-not (Test-Path $disk)) {
    & "$qemuDir\bin\qemu-img.exe" create -f raw $disk 64M
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
}

# Ports: 9999 is the remote desktop server (rdp 9999), host 2323 reaches the telnet program's
# default port 23. The Intel HD Audio card plays beep/play through the host's sound output.
#
# Acceleration: WHPX (the "Windows Hypervisor Platform" Windows feature) runs the guest on the
# CPU directly, several times faster than QEMU's emulator; where it is unavailable QEMU falls back
# to the emulator (tcg). WHPX cannot run -cpu max or host, so the CPU is a fixed model: Skylake
# has RDRAND/RDSEED for the kernel's random number generator. Under tcg QEMU warns about a few
# Skylake features it does not emulate, which is harmless.
$qemuArgs = @(
    '-L', "$qemuDir\share\qemu",
    '-accel', 'whpx', '-accel', 'tcg',
    '-M', 'q35', '-cpu', 'Skylake-Client-v4', '-m', '512M',
    '-serial', 'stdio',
    '-cdrom', '.\output-x64\RemSox.Kernel.iso', '-boot', 'd',
    '-drive', "file=$disk,if=none,id=disk0,format=raw", '-device', 'ide-hd,drive=disk0,bus=ide.0',
    '-vga', 'std',
    '-nic', 'user,hostfwd=tcp::9999-:9999,hostfwd=tcp::2323-:23',
    '-audiodev', 'dsound,id=snd0', '-device', 'intel-hda', '-device', 'hda-duplex,audiodev=snd0'
)

& "$qemuDir\bin\qemu-system-x86_64.exe" @qemuArgs
