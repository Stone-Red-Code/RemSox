"""Drive a RemSox VM from scripts (and from Claude): boot it in the background, read its serial
log, take screenshots, type keys and run shell commands over Telnet.

Usage (from the repository root):
    python tools/vm.py start [--build] [--window] [--fresh-disk | --no-disk] [--telnet]
    python tools/vm.py status
    python tools/vm.py stop
    python tools/vm.py serial [--tail N] [--grep REGEX] [--raw]
    python tools/vm.py wait-serial "text" [--timeout S]
    python tools/vm.py screenshot [path.png]
    python tools/vm.py type "some text\\n"      (\\n presses Enter, \\t Tab)
    python tools/vm.py key ctrl-c [ret ...]     (QEMU key names, as for the monitor's sendkey)
    python tools/vm.py click X Y [--button right] [--double]   (screen pixels, as in a screenshot)
    python tools/vm.py move X Y
    python tools/vm.py telnet "command" [--timeout S] [--idle S] [--user U --password P]
    python tools/vm.py monitor "info status"    (any QEMU HMP monitor command)

The VM is separate from build-and-run.ps1: it has its own disk (.vm/disk.img) and its own host
ports (Telnet 2324, remote desktop 9998, QEMU monitor 4444), so both can run at once.

RemSox is used only by someone logged in. `start --telnet` logs in at the console as the test
account below (creating it, as the first account and an administrator, on a disk that has
none), and `telnet` logs in as it for every command. They are test values for this VM's own
disk only.
"""

import argparse
import os
import re
import socket
import struct
import subprocess
import sys
import time
import zlib
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
KERNEL_DIR = ROOT / "RemSox.Kernel"
ISO = KERNEL_DIR / "output-x64" / "RemSox.Kernel.iso"
VM_DIR = ROOT / ".vm"
DISK = VM_DIR / "disk.img"
SERIAL_LOG = VM_DIR / "serial.log"
PID_FILE = VM_DIR / "qemu.pid"
QEMU_DIR = Path(os.environ.get("LOCALAPPDATA", "")) / "Cosmos" / "Tools" / "qemu"
QEMU = QEMU_DIR / "bin" / "qemu-system-x86_64.exe"
QEMU_IMG = QEMU_DIR / "bin" / "qemu-img.exe"

# The test account on the VM's disk; created by `start --telnet` on a disk without accounts.
VM_USER = "tester"
VM_PASSWORD = "vm-test-password"
# Exists once VM_USER was created on .vm/disk.img; removed with the disk by --fresh-disk.
ACCOUNT_MARKER = VM_DIR / "account-created"

MONITOR_PORT = 4444
TELNET_PORT = 2324
RDP_PORT = 9998

# Cosmos debug builds write these to the serial port many times a second; `serial` hides them.
SERIAL_NOISE = re.compile(
    r"^\[Kernel\] (Calling Run\(\)\.\.\.|Run\(\) returned)$|^\[LAPIC\] Timer tick|^\[STRIDE\] OnTick")
# Printed each time the main loop runs, so BeforeRun has finished and the shell is up.
BOOTED_MARKER = "[Kernel] Calling Run()"


# --- QEMU monitor -------------------------------------------------------------------------------

def monitor(command: str, timeout: float = 10.0) -> str:
    """Sends one HMP command to the monitor and returns its output."""
    with socket.create_connection(("127.0.0.1", MONITOR_PORT), timeout=timeout) as sock:
        _read_until(sock, b"(qemu) ", timeout)
        sock.sendall(command.encode() + b"\n")
        out = _read_until(sock, b"(qemu) ", timeout).decode(errors="replace")
    # The monitor echoes the command with terminal escapes; drop the echo and the prompt.
    out = re.sub(r"\x1b\[[0-9;]*[A-Za-z]", "", out).replace("\r", "")
    lines = [line for line in out.split("\n") if line.strip() and not line.startswith("(qemu)")]
    if lines and lines[0].strip().endswith(command.strip()):
        lines = lines[1:]
    return "\n".join(lines)


def _read_until(sock: socket.socket, marker: bytes, timeout: float) -> bytes:
    data = b""
    deadline = time.time() + timeout
    while marker not in data:
        if time.time() > deadline:
            raise TimeoutError(f"QEMU monitor did not answer: {data[-200:]!r}")
        chunk = sock.recv(4096)
        if not chunk:
            break
        data += chunk
    return data


def is_running() -> bool:
    try:
        monitor("info status", timeout=2)
        return True
    except OSError:
        return False


# --- Keyboard -----------------------------------------------------------------------------------

_PLAIN = {
    " ": "spc", "\n": "ret", "\t": "tab", "-": "minus", "=": "equal", ".": "dot", ",": "comma",
    "/": "slash", "\\": "backslash", ";": "semicolon", "'": "apostrophe", "[": "bracket_left",
    "]": "bracket_right", "`": "grave_accent",
}
_SHIFTED = {
    "!": "1", "@": "2", "#": "3", "$": "4", "%": "5", "^": "6", "&": "7", "*": "8", "(": "9",
    ")": "0", "_": "minus", "+": "equal", ":": "semicolon", '"': "apostrophe", "<": "comma",
    ">": "dot", "?": "slash", "|": "backslash", "{": "bracket_left", "}": "bracket_right",
    "~": "grave_accent",
}


def key_for(ch: str) -> str:
    if ch.isascii() and (ch.islower() or ch.isdigit()):
        return ch
    if ch.isascii() and ch.isupper():
        return "shift-" + ch.lower()
    if ch in _PLAIN:
        return _PLAIN[ch]
    if ch in _SHIFTED:
        return "shift-" + _SHIFTED[ch]
    raise ValueError(f"No key for {ch!r} (US layout)")


def type_text(text: str, delay: float = 0.03) -> None:
    for ch in text:
        monitor("sendkey " + key_for(ch))
        time.sleep(delay)


# --- Commands -----------------------------------------------------------------------------------

def cmd_start(args) -> int:
    if is_running():
        print("VM already running.")
        return start_telnet() if args.telnet else 0

    if args.build:
        print("Building (cosmos build)...", flush=True)
        result = subprocess.run(["cosmos", "build"], cwd=KERNEL_DIR)
        if result.returncode != 0:
            print("Build failed.")
            return result.returncode
    if not ISO.exists():
        print(f"No ISO at {ISO}; run with --build.")
        return 1

    VM_DIR.mkdir(exist_ok=True)
    if args.fresh_disk and DISK.exists():
        DISK.unlink()
    if not DISK.exists():
        ACCOUNT_MARKER.unlink(missing_ok=True)
    if not DISK.exists():
        subprocess.run([str(QEMU_IMG), "create", "-f", "raw", str(DISK), "64M"], check=True,
                       stdout=subprocess.DEVNULL)

    qemu_args = [
        str(QEMU), "-L", str(QEMU_DIR / "share" / "qemu"),
        "-accel", "whpx", "-accel", "tcg",
        "-M", "q35", "-cpu", "Skylake-Client-v4", "-m", "512M",
        "-serial", f"file:{SERIAL_LOG}",
        "-monitor", f"tcp:127.0.0.1:{MONITOR_PORT},server,nowait",
        "-cdrom", str(ISO), "-boot", "d",
        "-vga", "std",
        "-nic", f"user,hostfwd=tcp:127.0.0.1:{RDP_PORT}-:9999,hostfwd=tcp:127.0.0.1:{TELNET_PORT}-:23",
    ]
    if not args.no_disk:
        qemu_args += ["-drive", f"file={DISK},if=none,id=disk0,format=raw",
                      "-device", "ide-hd,drive=disk0,bus=ide.0"]
    if not args.window:
        qemu_args += ["-display", "none"]

    flags = getattr(subprocess, "CREATE_NEW_PROCESS_GROUP", 0) | getattr(subprocess, "DETACHED_PROCESS", 0)
    stderr = open(VM_DIR / "qemu.stderr.log", "w")
    proc = subprocess.Popen(qemu_args, stdout=subprocess.DEVNULL, stderr=stderr,
                            stdin=subprocess.DEVNULL, creationflags=flags)
    PID_FILE.write_text(str(proc.pid))

    deadline = time.time() + 15
    while not is_running():
        if proc.poll() is not None or time.time() > deadline:
            print("QEMU did not start; .vm/qemu.stderr.log says:")
            print((VM_DIR / "qemu.stderr.log").read_text(errors="replace"))
            return 1
        time.sleep(0.3)
    print(f"VM started (pid {proc.pid}). Serial log: {SERIAL_LOG}")

    return start_telnet() if args.telnet else 0


def telnet_up() -> bool:
    try:
        with socket.create_connection(("127.0.0.1", TELNET_PORT), timeout=2) as sock:
            sock.settimeout(3)
            return bool(sock.recv(1))
    except OSError:
        return False


def console_login() -> None:
    """
    Logs in at the console as VM_USER. On a disk without accounts the console asks for the
    first account instead (name, password, repeat), so it is created then and remembered in
    ACCOUNT_MARKER. Checking a password takes a second or two, hence the pauses.
    """
    time.sleep(1)
    if ACCOUNT_MARKER.exists():
        type_text(VM_USER + "\n")
        type_text(VM_PASSWORD + "\n")
    else:
        type_text(VM_USER + "\n")
        type_text(VM_PASSWORD + "\n")
        type_text(VM_PASSWORD + "\n")
        ACCOUNT_MARKER.write_text(VM_USER)
    time.sleep(4)


def start_telnet() -> int:
    """Waits for the console's login, logs in and types telnet."""
    if not wait_serial(BOOTED_MARKER, 120, quiet=True):
        print("The kernel did not reach Run() in time; take a screenshot to see why.")
        return 1
    if telnet_up():
        print(f"Telnet is up on 127.0.0.1:{TELNET_PORT}.")
        return 0
    console_login()
    type_text("telnet\n")
    deadline = time.time() + 15
    while time.time() < deadline:
        if telnet_up():
            print(f"Telnet is up on 127.0.0.1:{TELNET_PORT}.")
            return 0
        time.sleep(1)
    print("Telnet did not come up; take a screenshot to see why.")
    return 1


def cmd_stop(_args) -> int:
    if not is_running():
        print("VM not running.")
        return 0
    try:
        monitor("quit", timeout=3)
    except OSError:
        pass
    print("VM stopped.")
    return 0


def cmd_status(_args) -> int:
    print("running: " + monitor("info status") if is_running() else "not running")
    return 0


def cmd_serial(args) -> int:
    if not SERIAL_LOG.exists():
        print("No serial log yet.")
        return 1
    lines = SERIAL_LOG.read_text(errors="replace").splitlines()
    if not args.raw:
        lines = [line for line in lines if not SERIAL_NOISE.search(line)]
    if args.grep:
        lines = [line for line in lines if re.search(args.grep, line)]
    for line in lines[-args.tail:] if args.tail else lines:
        print(line)
    return 0


def wait_serial(text: str, timeout: float, quiet: bool = False) -> bool:
    deadline = time.time() + timeout
    while time.time() < deadline:
        if SERIAL_LOG.exists() and text in SERIAL_LOG.read_text(errors="replace"):
            return True
        time.sleep(0.5)
    if not quiet:
        print(f"'{text}' did not appear in the serial log within {timeout}s.")
    return False


def cmd_wait_serial(args) -> int:
    return 0 if wait_serial(args.text, args.timeout) else 1


def cmd_screenshot(args) -> int:
    path = Path(args.path or VM_DIR / "screen.png").resolve()
    path.parent.mkdir(parents=True, exist_ok=True)
    ppm = VM_DIR / "screen.ppm"
    if ppm.exists():
        ppm.unlink()
    out = monitor(f"screendump {ppm}")
    if out.strip() or not ppm.exists():
        print(out or "screendump wrote nothing")
        return 1
    path.write_bytes(_ppm_to_png(ppm.read_bytes()))
    print(path)
    return 0


def _ppm_to_png(data: bytes) -> bytes:
    """Converts QEMU's binary PPM (P6, maxval 255) to PNG; this QEMU is built without libpng."""
    fields, pos = [], 0
    while len(fields) < 4:
        while data[pos:pos + 1].isspace():
            pos += 1
        if data[pos:pos + 1] == b"#":
            pos = data.index(b"\n", pos) + 1
            continue
        end = pos
        while not data[end:end + 1].isspace():
            end += 1
        fields.append(data[pos:end])
        pos = end
    pos += 1
    width, height = int(fields[1]), int(fields[2])
    pixels = data[pos:pos + width * height * 3]
    stride = width * 3
    raw = b"".join(b"\x00" + pixels[y * stride:(y + 1) * stride] for y in range(height))

    def chunk(kind: bytes, body: bytes) -> bytes:
        return struct.pack(">I", len(body)) + kind + body + struct.pack(">I", zlib.crc32(kind + body))

    header = struct.pack(">IIBBBBB", width, height, 8, 2, 0, 0, 0)
    return (b"\x89PNG\r\n\x1a\n" + chunk(b"IHDR", header)
            + chunk(b"IDAT", zlib.compress(raw, 6)) + chunk(b"IEND", b""))


def cmd_type(args) -> int:
    type_text(args.text.encode().decode("unicode_escape"))
    return 0


def cmd_key(args) -> int:
    for key in args.keys:
        monitor("sendkey " + key)
        time.sleep(0.05)
    return 0


def mouse_to(x: int, y: int) -> None:
    """Moves the pointer to screen pixel (x, y). The mouse is PS/2, which only moves relatively,
    so the pointer is first pushed into the top-left corner, then moved in small steps."""
    for _ in range(20):
        monitor("mouse_move -100 -100")
    for dx, dy in _steps(x, y, 50):
        monitor(f"mouse_move {dx} {dy}")
    time.sleep(0.2)


def _steps(x: int, y: int, size: int):
    while x or y:
        dx, dy = max(-size, min(size, x)), max(-size, min(size, y))
        yield dx, dy
        x, y = x - dx, y - dy


def cmd_click(args) -> int:
    mouse_to(args.x, args.y)
    button = {"left": 1, "right": 2, "middle": 4}[args.button]
    for _ in range(2 if args.double else 1):
        monitor(f"mouse_button {button}")
        time.sleep(0.1)
        monitor("mouse_button 0")
        time.sleep(0.1)
    return 0


def cmd_move(args) -> int:
    mouse_to(args.x, args.y)
    return 0


def cmd_monitor(args) -> int:
    print(monitor(args.command, timeout=args.timeout))
    return 0


def cmd_telnet(args) -> int:
    """Logs in over Telnet, runs one command in that fresh session and prints what it writes."""
    with socket.create_connection(("127.0.0.1", TELNET_PORT), timeout=5) as sock:
        if not _telnet_login(sock, args.user, args.password):
            return 1
        sock.sendall(args.command.encode() + b"\r\n")
        output = _telnet_read(sock, idle=args.idle, timeout=args.timeout)
        try:
            sock.sendall(b"exit\r\n")
        except OSError:
            pass
    lines = _clean(output).split("\n")
    if lines and args.command in lines[0]:
        lines = lines[1:]  # the echoed command line
    print("\n".join(lines).rstrip())
    return 0


def _telnet_login(sock: socket.socket, user: str, password: str) -> bool:
    """Answers the login prompt; prints what the server said when the login fails."""
    seen = _telnet_read_until(sock, b"login: ", timeout=10)
    if b"login: " not in seen:
        print(_clean(seen).rstrip() or "No login prompt.")
        return False
    sock.sendall(user.encode() + b"\r\n")
    seen = _telnet_read_until(sock, b"password: ", timeout=10)
    sock.sendall(password.encode() + b"\r\n")
    seen = _telnet_read_until(sock, b"> ", timeout=20)
    if b"Logged in as" not in seen:
        print("Login failed: " + _clean(seen).strip())
        return False
    return True


def _telnet_read_until(sock: socket.socket, marker: bytes, timeout: float) -> bytes:
    """Reads until `marker` has arrived, the connection closes or `timeout` passes."""
    data = b""
    deadline = time.time() + timeout
    sock.settimeout(0.5)
    while time.time() < deadline and marker not in data:
        try:
            chunk = sock.recv(4096)
        except socket.timeout:
            continue
        if not chunk:
            break
        data += _negotiate(sock, chunk)
    return data


def _telnet_read(sock: socket.socket, idle: float, timeout: float) -> bytes:
    """Reads until nothing arrives for `idle` seconds, refusing any option negotiation."""
    data = b""
    deadline = time.time() + timeout
    sock.settimeout(idle)
    while time.time() < deadline:
        try:
            chunk = sock.recv(4096)
        except socket.timeout:
            break
        if not chunk:
            break
        data += _negotiate(sock, chunk)
    return data


def _negotiate(sock: socket.socket, chunk: bytes) -> bytes:
    IAC, DO, DONT, WILL, WONT, SB, SE = 255, 253, 254, 251, 252, 250, 240
    out = bytearray()
    i = 0
    while i < len(chunk):
        b = chunk[i]
        if b == IAC and i + 1 < len(chunk):
            op = chunk[i + 1]
            if op in (DO, DONT, WILL, WONT) and i + 2 < len(chunk):
                if op == DO:
                    sock.sendall(bytes([IAC, WONT, chunk[i + 2]]))
                elif op == WILL:
                    sock.sendall(bytes([IAC, DONT, chunk[i + 2]]))
                i += 3
                continue
            if op == SB:
                end = chunk.find(bytes([IAC, SE]), i)
                i = len(chunk) if end < 0 else end + 2
                continue
            i += 2
            continue
        out.append(b)
        i += 1
    return bytes(out)


def _clean(data: bytes) -> str:
    text = data.decode(errors="replace")
    text = re.sub(r"\x1b\[[0-9;?]*[A-Za-z]", "", text)
    return text.replace("\r\n", "\n").replace("\r", "\n")


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    sub = parser.add_subparsers(dest="cmd", required=True)

    p = sub.add_parser("start")
    p.add_argument("--build", action="store_true", help="run cosmos build first")
    p.add_argument("--window", action="store_true", help="show the QEMU window too")
    p.add_argument("--fresh-disk", action="store_true", help="start from a blank disk")
    p.add_argument("--no-disk", action="store_true", help="boot without any disk attached")
    p.add_argument("--telnet", action="store_true", help="type telnet once the shell is up")
    p.set_defaults(func=cmd_start)

    sub.add_parser("stop").set_defaults(func=cmd_stop)
    sub.add_parser("status").set_defaults(func=cmd_status)

    p = sub.add_parser("serial")
    p.add_argument("--tail", type=int, default=0, help="only the last N lines")
    p.add_argument("--grep", help="only lines matching this regex")
    p.add_argument("--raw", action="store_true", help="keep the per-tick noise lines")
    p.set_defaults(func=cmd_serial)

    p = sub.add_parser("wait-serial")
    p.add_argument("text")
    p.add_argument("--timeout", type=float, default=60)
    p.set_defaults(func=cmd_wait_serial)

    p = sub.add_parser("screenshot")
    p.add_argument("path", nargs="?")
    p.set_defaults(func=cmd_screenshot)

    p = sub.add_parser("type")
    p.add_argument("text")
    p.set_defaults(func=cmd_type)

    p = sub.add_parser("key")
    p.add_argument("keys", nargs="+")
    p.set_defaults(func=cmd_key)

    p = sub.add_parser("click")
    p.add_argument("x", type=int)
    p.add_argument("y", type=int)
    p.add_argument("--button", choices=["left", "right", "middle"], default="left")
    p.add_argument("--double", action="store_true")
    p.set_defaults(func=cmd_click)

    p = sub.add_parser("move")
    p.add_argument("x", type=int)
    p.add_argument("y", type=int)
    p.set_defaults(func=cmd_move)

    p = sub.add_parser("telnet")
    p.add_argument("command")
    p.add_argument("--timeout", type=float, default=20, help="give up after this many seconds")
    p.add_argument("--idle", type=float, default=1.5, help="output is done after this many quiet seconds")
    p.add_argument("--user", default=VM_USER, help="the account to log in as (default: the test account)")
    p.add_argument("--password", default=VM_PASSWORD, help="its password")
    p.set_defaults(func=cmd_telnet)

    p = sub.add_parser("monitor")
    p.add_argument("command")
    p.add_argument("--timeout", type=float, default=10)
    p.set_defaults(func=cmd_monitor)

    args = parser.parse_args()
    return args.func(args)


if __name__ == "__main__":
    sys.exit(main())
