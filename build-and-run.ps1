Set-Location $PSScriptRoot

cosmos build
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

#cosmos run
bash -c "qemu-system-x86_64 -M q35 -cpu max -m 512M -serial stdio -cdrom ./output-x64/RemSox.iso -vga std -nic user,hostfwd=tcp::9999-:9999"