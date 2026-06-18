cosmos build
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

#cosmos run
& "$env:LOCALAPPDATA\Cosmos\Tools\qemu\bin\qemu-system-x86_64.exe" -L "$env:LOCALAPPDATA\Cosmos\Tools\qemu\share\qemu" -M q35 -cpu max -m 512M -serial stdio -cdrom .\output-x64\RemSox.Kernel.iso -vga std -nic user,hostfwd=tcp::9999-:9999
