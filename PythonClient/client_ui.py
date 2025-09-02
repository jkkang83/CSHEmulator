# python_client.py
# ------------------------------------------------------------
# Win/Tkinter UI + TCP client (C# 서버와 동일 프로토콜)
# - Text frames: "CMD\r\n" or "CMD@arg@...\r\n"
# - Binary frames:
#     A_D@len@<bytes>@\r\n
#     A_R@frmCnt@<bytes>@\r\n  (payload = 44 + frmCnt*8*6)
# - Auto reconnect with exponential backoff
# ------------------------------------------------------------

import asyncio
import struct
import threading
import tkinter as tk
from tkinter import scrolledtext

# ============================================================
# NetworkClient (asyncio) with auto-reconnect
# ============================================================

class NetworkClient:
    """TCP client with C#-like callbacks:
       - on_log(str)
       - on_receive(bytes)  # raw frame including trailing CRLF
       Protocol:
         Text  : "CMD\r\n" or "CMD@arg@...\r\n"
         Binary: "A_D@len@<bytes>@\r\n"
                 "A_R@frmCnt@<bytes>@\r\n"
       Auto-reconnect with exponential backoff.
    """

    def __init__(self):
        self.reader = None               # asyncio.StreamReader
        self.writer = None               # asyncio.StreamWriter
        self.on_log = None               # callable(str)
        self.on_receive = None           # callable(bytes)
        self._buf = bytearray()
        self._read_timeout = 30

        # reconnect state
        self._want_run = False
        self._runner_task = None         # asyncio.Task
        self._host = None
        self._port = None
        self._max_backoff = 10.0

    # ---- state ----
    def is_connected(self) -> bool:
        return self.writer is not None and not self.writer.is_closing()

    # ---- public api ----
    async def start_auto_reconnect(self, host: str, port: int):
        """Start/refresh background runner that will connect and auto-reconnect."""
        self._host, self._port = host, port
        self._want_run = True
        if self._runner_task is None or self._runner_task.done():
            self._runner_task = asyncio.create_task(self._runner())

    async def stop_client(self):
        """Stop runner and close current connection."""
        self._want_run = False
        try:
            if self.writer:
                self.writer.close()
                await self.writer.wait_closed()
        except Exception:
            pass
        self.reader = None
        self.writer = None
        if self._runner_task:
            try:
                await asyncio.wait_for(self._runner_task, timeout=0.1)
            except Exception:
                pass
        self._runner_task = None

    # kept for compatibility
    async def start_client(self, host: str, port: int):
        await self.start_auto_reconnect(host, port)

    # ---- sending ----
    async def _send_raw(self, data: bytes):
        if not self.is_connected():
            self._log("Send failed: not connected")
            return
        try:
            self.writer.write(data)
            await self.writer.drain()
        except Exception as e:
            self._log(f"Send error: {e}")

    async def send_cmd(self, cmd: str):
        await self._send_raw((cmd + "\r\n").encode("ascii"))

    async def send_ascii(self, *parts: str):
        await self._send_raw(("@".join(parts) + "\r\n").encode("ascii"))

    async def send_a_d(self, payload: bytes):
        head = f"A_D@{len(payload)}@".encode("ascii")
        tail = b"@\r\n"
        await self._send_raw(head + payload + tail)

    # ---- runner & receive loop ----
    async def _runner(self):
        backoff = 1.0
        while self._want_run:
            try:
                await self._connect_once()
                backoff = 1.0
                await self._receive_loop()
            except Exception as e:
                self._log(f"[runner] error: {e}")

            if not self._want_run:
                break
            self._log(f"Disconnected. Reconnecting in {backoff:.1f}s ...")
            try:
                await asyncio.sleep(backoff)
            except asyncio.CancelledError:
                break
            backoff = min(backoff * 2.0, self._max_backoff)

        try:
            if self.writer:
                self.writer.close()
                await self.writer.wait_closed()
        except Exception:
            pass
        self.reader = None
        self.writer = None
        self._log("Runner stopped")

    async def _connect_once(self):
        if not self._host or not self._port:
            raise RuntimeError("Host/Port not set")
        try:
            if self.writer:
                self.writer.close()
                await self.writer.wait_closed()
        except Exception:
            pass
        self.reader = self.writer = None
        self.reader, self.writer = await asyncio.open_connection(self._host, self._port)
        self._log(f"Connected to {self._host}:{self._port}")

    async def _receive_loop(self):
        self._buf.clear()
        try:
            while self.is_connected() and self._want_run:
                try:
                    chunk = await asyncio.wait_for(self.reader.read(8192), timeout=self._read_timeout)
                except asyncio.TimeoutError:
                    self._log("Receive timeout")
                    break

                if not chunk:
                    break  # EOF
                self._buf.extend(chunk)

                while True:
                    frame = self._try_extract_frame(self._buf)
                    if frame is None:
                        break
                    try:
                        if self.on_receive:
                            self.on_receive(frame)
                    except Exception as cb_ex:
                        self._log(f"[WARN] on_receive callback error: {cb_ex}")
        finally:
            try:
                if self.writer:
                    self.writer.close()
                    await self.writer.wait_closed()
            except Exception:
                pass
            self.reader = None
            self.writer = None

    # ---- framing ----
    @staticmethod
    def _find_crlf(buf: bytearray, start: int = 0) -> int:
        i = buf.find(b"\r\n", start)
        return i if i >= 0 else -1

    @staticmethod
    def _index_of(buf: bytearray, byte_value: int, start: int = 0) -> int:
        i = buf.find(bytes([byte_value]), start)
        return i if i >= 0 else -1

    @staticmethod
    def _get_ascii(buf: bytearray, start: int, end_ex: int) -> str:
        return buf[start:end_ex].decode("ascii", errors="replace")

    @classmethod
    def _take(cls, buf: bytearray, count: int) -> bytes:
        out = bytes(buf[:count])
        del buf[:count]
        return out

    def _try_extract_frame(self, buf: bytearray):
        if not buf:
            return None

        at_idx = self._index_of(buf, ord('@'), 0)
        crlf_idx = self._find_crlf(buf, 0)

        # pure "CMD\r\n"
        if crlf_idx >= 0 and (at_idx < 0 or crlf_idx < at_idx):
            return self._take(buf, crlf_idx + 2)

        if at_idx >= 0:
            cmd = self._get_ascii(buf, 0, at_idx)

            # A_D@len@<bytes>@\r\n
            if cmd == "A_D":
                len_start = at_idx + 1
                second_at = self._index_of(buf, ord('@'), len_start)
                if second_at < 0:
                    return None
                len_str = self._get_ascii(buf, len_start, second_at)
                try:
                    data_len = int(len_str)
                    if data_len < 0:
                        raise ValueError
                except Exception:
                    del buf[0:1]
                    return None
                data_start = second_at + 1
                after_data = data_start + data_len
                if len(buf) < after_data + 3:
                    return None
                if not (buf[after_data] == ord('@') and buf[after_data + 1:after_data + 3] == b"\r\n"):
                    del buf[0:1]
                    return None
                return self._take(buf, after_data + 3)

            # A_R@frmCnt@<bytes>@\r\n
            if cmd == "A_R":
                cnt_start = at_idx + 1
                second_at = self._index_of(buf, ord('@'), cnt_start)
                if second_at < 0:
                    return None
                cnt_str = self._get_ascii(buf, cnt_start, second_at)
                try:
                    frm_cnt = int(cnt_str)
                    if frm_cnt < 0:
                        raise ValueError
                except Exception:
                    del buf[0:1]
                    return None

                data_len = 44 + frm_cnt * 8 * 6
                data_start = second_at + 1
                after_data = data_start + data_len
                if len(buf) < after_data + 3:
                    return None
                if not (buf[after_data] == ord('@') and buf[after_data + 1:after_data + 3] == b"\r\n"):
                    del buf[0:1]
                    return None
                return self._take(buf, after_data + 3)

            # generic text: wait CRLF
            if crlf_idx < 0:
                return None
            return self._take(buf, crlf_idx + 2)

        return None

    # ---- logging ----
    def _log(self, s: str):
        if self.on_log:
            self.on_log(s)
        else:
            print(s)


# ============================================================
# Tkinter UI (C# WinForms-like)
# ============================================================

class ClientApp:
    def __init__(self, root: tk.Tk):
        self.root = root
        self.root.title("Python TCP Client")

        self.client = NetworkClient()
        self.client.on_log = self._log
        self.client.on_receive = self._on_receive

        # ---- header row ----
        top = tk.Frame(root)
        top.pack(fill="x", padx=8, pady=6)

        self.btn_clear = tk.Button(top, text="ClearLog", width=12, command=self.clear_log)
        self.btn_clear.pack(side="left")

        tk.Label(top, text="IP Address").pack(side="left", padx=(16, 4))
        self.txt_host = tk.Entry(top, width=12)
        self.txt_host.insert(0, "127.0.0.1")
        self.txt_host.pack(side="left")

        tk.Label(top, text="Port :").pack(side="left", padx=(16, 4))
        self.txt_port = tk.Entry(top, width=6)
        self.txt_port.insert(0, "5000")
        self.txt_port.pack(side="left")

        self.lamp = tk.Canvas(top, width=18, height=18, highlightthickness=0)
        self._lamp_id = self.lamp.create_rectangle(2, 2, 16, 16, fill="#cc0000")
        self.lamp.pack(side="right")

        # ---- left column (controls) ----
        left = tk.Frame(root)
        left.pack(side="left", padx=8, pady=4, anchor="n")

        tk.Label(left, text="Frame Count").pack(pady=(0, 4))
        self.txt_frame = tk.Entry(left, width=6, justify="center")
        self.txt_frame.insert(0, "1")
        self.txt_frame.pack(pady=(0, 12))

        self._mk_btn(left, "P_S", lambda: self._send_cmd("P_S")).pack(pady=6)
        self._mk_btn(left, "R_C", self._send_rc).pack(pady=6)
        self._mk_btn(left, "R_S", self._send_rs).pack(pady=6)
        self._mk_btn(left, "D_S", lambda: self._send_cmd("D_S")).pack(pady=6)
        self._mk_btn(left, "M_S", lambda: self._send_cmd("M_S")).pack(pady=6)

        # ---- log area ----
        right = tk.Frame(root)
        right.pack(side="left", fill="both", expand=True, padx=(0, 8), pady=4)

        self.log = scrolledtext.ScrolledText(
            right, wrap="none", bg="#0b0b0f", fg="#d9d9d9", insertbackground="#d9d9d9"
        )
        self.log.pack(fill="both", expand=True)

        # ---- background asyncio loop thread ----
        self.loop = asyncio.new_event_loop()
        self.loop_thread = threading.Thread(target=self.loop.run_forever, daemon=True)
        self.loop_thread.start()

        # start auto connect
        self.root.after(50, self.auto_connect)
        # start lamp updater
        self._update_lamp()

        # cleanup on close
        self.root.protocol("WM_DELETE_WINDOW", self.on_close)

    # ---------- helpers ----------
    def _mk_btn(self, parent, text, cmd):
        return tk.Button(parent, text=text, width=10, command=cmd)

    def _post_coro(self, coro):
        asyncio.run_coroutine_threadsafe(coro, self.loop)

    def _log(self, s: str):
        self.root.after(0, lambda: self._append(f"{s}\r\n"))

    def _append(self, s: str):
        self.log.insert("end", s)
        self.log.see("end")

    def clear_log(self):
        self.log.delete("1.0", "end")

    # ---------- lamp ----------
    def _update_lamp(self):
        color = "#1976d2" if self.client.is_connected() else "#cc0000"
        self.lamp.itemconfigure(self._lamp_id, fill=color)
        self.root.after(300, self._update_lamp)

    # ---------- connect ----------
    def auto_connect(self):
        host = (self.txt_host.get() or "127.0.0.1").strip()
        try:
            port = int((self.txt_port.get() or "5000").strip())
        except ValueError:
            port = 5000
        self._post_coro(self.client.start_auto_reconnect(host, port))

    # ---------- send ----------
    def _send_cmd(self, cmd: str):
        self._post_coro(self.client.send_cmd(cmd))

    def _send_rs(self):
        n = self.txt_frame.get().strip() or "1"
        self._post_coro(self.client.send_ascii("R_S", n))

    def _send_rc(self):
        n = self.txt_frame.get().strip() or "1"
        self._post_coro(self.client.send_ascii("R_C", n))

    # ---------- receive handler ----------
    def _on_receive(self, frame: bytes):
        # find first '@' and CRLF
        try:
            first_at = frame.find(b'@')
            crlf = frame.find(b"\r\n")
            if first_at < 0 and crlf >= 0:
                line = frame[:crlf].decode("ascii", errors="replace")
                self._log("RX: " + line)
                return
            if first_at < 0:
                self._log(f"[WARN] Unknown frame (no '@') len={len(frame)}")
                return

            cmd = frame[:first_at].decode("ascii", errors="replace")

            # A_D@len@<bytes>@\r\n
            if cmd == "A_D":
                second_at = frame.find(b'@', first_at + 1)
                if second_at < 0:
                    self._log("[WARN] A_D incomplete header")
                    return
                len_str = frame[first_at + 1:second_at].decode("ascii", errors="replace")
                try:
                    data_len = int(len_str)
                except Exception:
                    self._log("[WARN] A_D len parse fail")
                    return
                data_start = second_at + 1
                tail = data_start + data_len
                if not (len(frame) >= tail + 3 and frame[tail:tail + 3] == b"@\r\n"):
                    self._log("[WARN] A_D tail invalid")
                    return
                payload = frame[data_start:tail]

                if data_len % 8 != 0:
                    self._log(f"[WARN] A_D payload size({data_len}) not multiple of 8")

                doubles = [struct.unpack_from("<d", payload, i)[0] for i in range(0, len(payload), 8)]
                self._log(f"A_D Recieve (len={data_len}, doubles={len(doubles)})")
                if len(doubles) == 6:
                    self._log(f"  X={doubles[0]:.3f}, Y={doubles[1]:.3f}, Z={doubles[2]:.3f}, "
                              f"TX={doubles[3]:.3f}, TY={doubles[4]:.3f}, TZ={doubles[5]:.3f}")
                else:
                    for i, v in enumerate(doubles):
                        self._log(f"  [{i}] {v:.3f}")
                return

            # A_R@frmCnt@<bytes>@\r\n
            if cmd == "A_R":
                second_at = frame.find(b'@', first_at + 1)
                if second_at < 0:
                    self._log("[WARN] A_R incomplete header")
                    return
                cnt_str = frame[first_at + 1:second_at].decode("ascii", errors="replace")
                try:
                    frm_cnt = int(cnt_str)
                except Exception:
                    self._log("[WARN] A_R frmCnt parse fail")
                    return

                data_len = 44 + frm_cnt * 8 * 6
                data_start = second_at + 1
                tail = data_start + data_len
                if not (len(frame) >= tail + 3 and frame[tail:tail + 3] == b"@\r\n"):
                    self._log("[WARN] A_R tail invalid")
                    return
                payload = frame[data_start:tail]

                off = 0
                sTime = struct.unpack_from("<q", payload, off)[0]; off += 8
                frameCount = struct.unpack_from("<i", payload, off)[0]; off += 4
                fps  = struct.unpack_from("<d", payload, off)[0]; off += 8
                ledL = struct.unpack_from("<d", payload, off)[0]; off += 8
                ledR = struct.unpack_from("<d", payload, off)[0]; off += 8
                ttime= struct.unpack_from("<d", payload, off)[0]; off += 8

                def read_arr(n):
                    arr = [struct.unpack_from("<d", payload, off + 8*i)[0] for i in range(n)]
                    return arr

                X  = read_arr(frm_cnt); off += 8*frm_cnt
                Y  = read_arr(frm_cnt); off += 8*frm_cnt
                Z  = read_arr(frm_cnt); off += 8*frm_cnt
                TX = read_arr(frm_cnt); off += 8*frm_cnt
                TY = read_arr(frm_cnt); off += 8*frm_cnt
                TZ = read_arr(frm_cnt); off += 8*frm_cnt

                self._log(f"A_R Recieve (frmCnt={frm_cnt}, bytes={data_len})  "
                          f"fps={fps:.2f}, LED(L/R)={ledL:.2f}/{ledR:.2f}, testTime={ttime}")
                for i in range(frm_cnt):
                    self._log(f"  [{i}] X={X[i]:.2f}, Y={Y[i]:.2f}, Z={Z[i]:.2f}, "
                              f"TX={TX[i]:.2f}, TY={TY[i]:.2f}, TZ={TZ[i]:.2f}")
                return

            # A_M@<id>@\r\n
            if cmd == "A_M":
                second_at = frame.find(b'@', first_at + 1)
                if second_at < 0:
                    self._log("[WARN] A_M missing 2nd '@'")
                    return
                mark_id = frame[first_at + 1:second_at].decode("ascii", errors="replace")
                self._log(f"A_M Recieve (Mark ID = {mark_id})")
                return

            # generic text
            line = frame[:crlf].decode("ascii", errors="replace") if crlf >= 0 \
                else frame.decode("ascii", errors="replace")
            self._log("RX: " + line.strip())

        except Exception as ex:
            self._log(f"[RX ERR] {ex}")

    # ---------- close ----------
    def on_close(self):
        try:
            self._post_coro(self.client.stop_client())
        except Exception:
            pass
        self.loop.call_soon_threadsafe(self.loop.stop)
        self.loop_thread.join(timeout=1.0)
        self.root.destroy()


# ============================================================
# main
# ============================================================

if __name__ == "__main__":
    root = tk.Tk()
    app = ClientApp(root)
    root.geometry("840x560")
    root.mainloop()
