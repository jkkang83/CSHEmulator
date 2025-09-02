# python_client.py
# -*- coding: utf-8 -*-
#
# Python Tkinter client compatible with the C# server
# - Auto connect + auto reconnect (only if connection is lost)
# - Protocol: "CMD@arg@...@\r\n", A_D, A_R, A_M
# - Tkinter UI with buttons, frame count, status lamp, and log window

import socket
import threading
import time
import struct
import tkinter as tk
from tkinter import ttk


# ==============================
# Frame Parser
# ==============================
class FrameParser:
    @staticmethod
    def try_extract(src: bytearray):
        """Try to extract one complete frame from buffer"""
        if len(src) < 2:
            return None

        # Find CRLF
        crlf = -1
        for i in range(len(src) - 1):
            if src[i] == 0x0D and src[i + 1] == 0x0A:
                crlf = i
                break
        if crlf < 0:
            return None

        # Look for '@'
        try:
            at1 = src.index(ord('@'), 0, crlf)
        except ValueError:
            frame = bytes(src[:crlf + 2])
            del src[:crlf + 2]
            return frame

        cmd = src[:at1].decode('ascii', errors='ignore')
        if cmd in ('A_D', 'A_R'):
            try:
                at2 = src.index(ord('@'), at1 + 1)
            except ValueError:
                return None

            try:
                num_str = src[at1 + 1:at2].decode('ascii')
                token = int(num_str)
                if token < 0:
                    del src[:1]
                    return None
            except Exception:
                del src[:1]
                return None

            # Look for "@\r\n" tail
            tail = src.find(b'@\r\n', at2)
            if tail < 0:
                return None
            frame = bytes(src[:tail + 3])
            del src[:tail + 3]
            return frame

        # Fallback: plain ASCII line
        frame = bytes(src[:crlf + 2])
        del src[:crlf + 2]
        return frame


# ==============================
# Reconnecting TCP Client
# ==============================
class ReconnectingClient:
    def __init__(self, log_cb, frame_cb):
        self._log_cb = log_cb
        self._frame_cb = frame_cb
        self._sock = None
        self._send_lock = threading.Lock()
        self._stop_evt = threading.Event()
        self._connected = False
        self._host = '127.0.0.1'
        self._port = 5000
        self._thread = None

    @property
    def is_connected(self):
        return self._connected

    def start(self, host, port):
        """Start connection with auto-reconnect"""
        self._host = host.strip()
        self._port = int(port)
        if self._thread and self._thread.is_alive():
            return
        self._stop_evt.clear()
        self._thread = threading.Thread(target=self._runner, daemon=True)
        self._thread.start()

    def stop(self):
        """Stop connection"""
        self._stop_evt.set()
        try:
            if self._sock:
                self._sock.close()
        except Exception:
            pass
        if self._thread:
            self._thread.join(timeout=2.0)

    # --- Send API ---
    def send_cmd(self, cmd: str):
        """Send command without arguments, always append '@\\r\\n'"""
        if not cmd.endswith('@'):
            cmd += '@'
        if not cmd.endswith("\r\n"):
            cmd += "\r\n"
        self._send_raw(cmd.encode('ascii', errors='ignore'))

    def send_ascii(self, cmd: str, *args: str):
        """Send tokenized command: CMD@arg@...@\\r\\n"""
        parts = [cmd or '']
        for a in args:
            parts.append('@')
            parts.append(a or '')
        parts.append('@\r\n')
        data = ''.join(parts).encode('ascii', errors='ignore')
        self._send_raw(data)

    def _log(self, s):
        try:
            self._log_cb(s)
        except Exception:
            pass

    def _send_raw(self, data: bytes):
        try:
            with self._send_lock:
                if not self._sock:
                    raise RuntimeError('Not connected')
                self._sock.sendall(data)
        except Exception as ex:
            self._log(f'[TX] send error: {ex}\r\n')

    # --- Main loop ---
    def _runner(self):
        backoff = 0.5
        parser = FrameParser()
        ring = bytearray()
        while not self._stop_evt.is_set():
            try:
                self._log(f'[Client] Connecting to {self._host}:{self._port}...\r\n')
                s = socket.create_connection((self._host, self._port), timeout=5.0)
                s.setsockopt(socket.IPPROTO_TCP, socket.TCP_NODELAY, 1)
                s.settimeout(1.0)
                self._sock = s
                self._connected = True
                self._log('[Client] Connected\r\n')
                backoff = 0.5
                ring.clear()
                while not self._stop_evt.is_set():
                    try:
                        chunk = s.recv(8192)
                        if not chunk:
                            self._log('[RX] remote closed\r\n')
                            break
                        ring.extend(chunk)
                        while True:
                            frame = parser.try_extract(ring)
                            if frame is None:
                                break
                            try:
                                self._frame_cb(frame)
                            except Exception:
                                import traceback; traceback.print_exc()
                    except socket.timeout:
                        continue
            except Exception as ex:
                self._log(f'[Client] connect/read error: {ex}\r\n')
            finally:
                self._connected = False
                try:
                    if self._sock:
                        self._sock.close()
                except Exception:
                    pass
                self._sock = None
            if self._stop_evt.is_set():
                break
            time.sleep(backoff)
            backoff = min(backoff * 2.0, 5.0)


# ==============================
# Tkinter UI
# ==============================
class App(tk.Tk):
    def __init__(self):
        super().__init__()
        self.title('Python Client for C# Server')
        self.geometry('920x560')
        self.protocol('WM_DELETE_WINDOW', self.on_close)

        # Connection panel
        frm = ttk.Frame(self)
        frm.pack(side=tk.TOP, fill=tk.X, padx=8, pady=8)
        ttk.Label(frm, text='IP Address').grid(row=0, column=0, sticky='w')
        self.txt_host = ttk.Entry(frm, width=18); self.txt_host.grid(row=0, column=1, padx=6); self.txt_host.insert(0, '127.0.0.1')
        ttk.Label(frm, text='Port').grid(row=0, column=2, sticky='w')
        self.txt_port = ttk.Entry(frm, width=8); self.txt_port.grid(row=0, column=3, padx=6); self.txt_port.insert(0, '5000')
        ttk.Label(frm, text='Frame Count').grid(row=0, column=4, sticky='w')
        self.txt_frames = ttk.Entry(frm, width=8); self.txt_frames.grid(row=0, column=5, padx=6); self.txt_frames.insert(0, '1')

        # Status lamp
        self.lamp = tk.Canvas(frm, width=18, height=18, highlightthickness=0)
        self.lamp.grid(row=0, column=6, padx=10)
        self._lamp_id = self.lamp.create_oval(2, 2, 16, 16, fill='red', outline='black')

        # Buttons
        cmdfrm = ttk.Frame(self); cmdfrm.pack(side=tk.LEFT, fill=tk.Y, padx=8, pady=6)
        self.btn_ps = ttk.Button(cmdfrm, text='P_S', width=10, command=self.send_ps)
        self.btn_rc = ttk.Button(cmdfrm, text='R_C', width=10, command=self.send_rc)
        self.btn_rs = ttk.Button(cmdfrm, text='R_S', width=10, command=self.send_rs)
        self.btn_ds = ttk.Button(cmdfrm, text='D_S', width=10, command=self.send_ds)
        self.btn_ms = ttk.Button(cmdfrm, text='M_S', width=10, command=self.send_ms)
        self.btn_clear = ttk.Button(cmdfrm, text='Clear Log', width=10, command=self.clear_log)
        for b in [self.btn_ps, self.btn_rc, self.btn_rs, self.btn_ds, self.btn_ms, self.btn_clear]:
            b.pack(pady=4)

        # Log window
        logfrm = ttk.Frame(self); logfrm.pack(side=tk.RIGHT, fill=tk.BOTH, expand=True, padx=8, pady=8)
        self.txt_log = tk.Text(logfrm, wrap='none', font=('Consolas', 10), bg='black', fg='yellow')
        self.txt_log.pack(side=tk.LEFT, fill=tk.BOTH, expand=True)
        vs = ttk.Scrollbar(logfrm, orient='vertical', command=self.txt_log.yview)
        self.txt_log.configure(yscrollcommand=vs.set); vs.pack(side=tk.RIGHT, fill=tk.Y)

        # Client
        self.client = ReconnectingClient(self._on_log, self._on_frame)
        self.after(500, self._tick_lamp)
        self.after(100, self.auto_connect)

    def _on_log(self, s: str):
        ts = time.strftime('%Y-%m-%d %H:%M:%S')
        self.txt_log.insert('end', f'{ts}, {s}')
        self.txt_log.see('end')

    def _on_frame(self, frame: bytes):
        """Parse and log received frame"""
        crlf = frame.find(b'\r\n')
        first_at = frame.find(b'@')
        if first_at <= 0:
            if crlf >= 0:
                line = frame[:crlf].decode('ascii', errors='ignore')
                self._on_log(f'RX: {line}\r\n')
            return

        cmd = frame[:first_at].decode('ascii', errors='ignore')

        # --- Handle A_D ---
        if cmd == 'A_D':
            second_at = frame.find(b'@', first_at + 1)
            if second_at < 0: return
            s_count = frame[first_at + 1:second_at].decode('ascii', errors='ignore')
            try:
                cnt = int(s_count)
            except: return
            tail = frame.rfind(b'@\r\n')
            payload = frame[second_at + 1:tail]
            vals = struct.unpack('<' + 'd' * (len(payload)//8), payload)
            self._on_log(f'A_D Receive (count={cnt}, doubles={len(vals)})\r\n')
            for i, v in enumerate(vals):
                self._on_log(f'  [{i}] {v:.3f}\r\n')
            return

        # --- Handle A_R ---
        if cmd == 'A_R':
            second_at = frame.find(b'@', first_at + 1)
            if second_at < 0: return
            s_cnt = frame[first_at + 1:second_at].decode('ascii', errors='ignore')
            try:
                frm_cnt = int(s_cnt)
            except: return
            tail = frame.rfind(b'@\r\n')
            payload = frame[second_at + 1:tail]
            if len(payload) < 44: return
            off = 0
            def read_i64(): nonlocal off; v=struct.unpack_from('<q', payload, off)[0]; off+=8; return v
            def read_i32(): nonlocal off; v=struct.unpack_from('<i', payload, off)[0]; off+=4; return v
            def read_d(): nonlocal off; v=struct.unpack_from('<d', payload, off)[0]; off+=8; return v
            s_time = read_i64(); frameCt = read_i32(); fps = read_d(); ledL = read_d(); ledR = read_d(); testT = read_d()
            X = [read_d() for _ in range(frm_cnt)]
            Y = [read_d() for _ in range(frm_cnt)]
            Z = [read_d() for _ in range(frm_cnt)]
            TX = [read_d() for _ in range(frm_cnt)]
            TY = [read_d() for _ in range(frm_cnt)]
            TZ = [read_d() for _ in range(frm_cnt)]
            self._on_log(f'A_R Receive (frames={frm_cnt}, fps={fps:.2f}, test={testT:.2f}s)\r\n')
            for i in range(frm_cnt):
                self._on_log(f'  [{i}] X={X[i]:.2f}, Y={Y[i]:.2f}, Z={Z[i]:.2f}, TX={TX[i]:.2f}, TY={TY[i]:.2f}, TZ={TZ[i]:.2f}\r\n')
            return

        # --- Handle A_M ---
        if cmd == 'A_M':
            second_at = frame.find(b'@', first_at + 1)
            if second_at < 0: return
            mark_id = frame[first_at + 1:second_at].decode('ascii', errors='ignore')
            self._on_log(f'A_M Receive (Mark ID={mark_id})\r\n')
            return

        # --- Fallback ASCII ---
        if crlf >= 0:
            line = frame[:crlf].decode('ascii', errors='ignore')
            self._on_log(f'RX: {line}\r\n')

    def _tick_lamp(self):
        fill = 'blue' if self.client.is_connected else 'red'
        self.lamp.itemconfig(self._lamp_id, fill=fill)
        self.after(500, self._tick_lamp)

    def auto_connect(self):
        host = self.txt_host.get().strip() or "127.0.0.1"
        try:
            port = int(self.txt_port.get().strip() or "5000")
        except ValueError:
            port = 5000
        self.client.start(host, port)

    # --- Button Events ---
    def send_ps(self): self.client.send_ascii('P_S')
    def send_rs(self): self.client.send_ascii('R_S', self.txt_frames.get().strip() or '1')
    def send_rc(self): self.client.send_ascii('R_C', self.txt_frames.get().strip() or '1')
    def send_ds(self): self.client.send_ascii('D_S')
    def send_ms(self): self.client.send_ascii('M_S')
    def clear_log(self): self.txt_log.delete('1.0', 'end')

    def on_close(self):
        try: self.client.stop()
        finally: self.destroy()


if __name__ == '__main__':
    App().mainloop()
