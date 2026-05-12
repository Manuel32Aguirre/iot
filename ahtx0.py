import time


class AHT20:
    _CMD_INITIALIZE = b"\xBE\x08\x00"
    _CMD_TRIGGER = b"\xAC\x33\x00"
    _CMD_SOFTRESET = b"\xBA"
    _STATUS_BUSY = 0x80
    _STATUS_CALIBRATED = 0x08

    def __init__(self, i2c, address=0x38):
        self.i2c = i2c
        self.address = address
        self._buffer = bytearray(6)
        self.reset()
        self._ensure_ready()

    def reset(self):
        self.i2c.writeto(self.address, self._CMD_SOFTRESET)
        time.sleep_ms(20)

    def _status(self):
        return self.i2c.readfrom(self.address, 1)[0]

    def _ensure_ready(self):
        if not (self._status() & self._STATUS_CALIBRATED):
            self.i2c.writeto(self.address, self._CMD_INITIALIZE)
            time.sleep_ms(10)

    def _wait_for_idle(self, timeout_ms=200):
        start = time.ticks_ms()
        while self._status() & self._STATUS_BUSY:
            if time.ticks_diff(time.ticks_ms(), start) > timeout_ms:
                raise OSError("AHT20 timeout")
            time.sleep_ms(5)

    def _measure(self):
        self._ensure_ready()
        self.i2c.writeto(self.address, self._CMD_TRIGGER)
        time.sleep_ms(80)
        self._wait_for_idle()
        self.i2c.readfrom_into(self.address, self._buffer)

    @property
    def relative_humidity(self):
        self._measure()
        raw = ((self._buffer[1] << 12) | (self._buffer[2] << 4) | (self._buffer[3] >> 4))
        return (raw * 100.0) / 1048576.0

    @property
    def temperature(self):
        self._measure()
        raw = (((self._buffer[3] & 0x0F) << 16) | (self._buffer[4] << 8) | self._buffer[5])
        return ((raw * 200.0) / 1048576.0) - 50.0