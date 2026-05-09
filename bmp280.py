from machine import I2C
import struct

class BMP280:
    def __init__(self, i2c, address=0x77):
        self.i2c = i2c
        self.address = address
        try:
            self.i2c.readfrom(self.address, 1)
        except:
            raise Exception(f"Sensor BMP280 no encontrado en 0x{address:02x}")

        # Leer coeficientes de calibración
        calib = self.i2c.readfrom_mem(self.address, 0x88, 24)
        self.dig_T1, self.dig_T2, self.dig_T3 = struct.unpack("<Hhh", calib[0:6])
        self.dig_P1, self.dig_P2, self.dig_P3, self.dig_P4, self.dig_P5, \
        self.dig_P6, self.dig_P7, self.dig_P8, self.dig_P9 = struct.unpack("<Hhhhhhhhh", calib[6:24])
        self.t_fine = 0

    def get_temperature(self):
        raw = self.i2c.readfrom_mem(self.address, 0xFA, 3)
        adc_t = (raw[0] << 12) | (raw[1] << 4) | (raw[2] >> 4)
        var1 = (((adc_t >> 3) - (self.dig_T1 << 1)) * self.dig_T2) >> 11
        var2 = (((((adc_t >> 4) - self.dig_T1) * ((adc_t >> 4) - self.dig_T1)) >> 12) * self.dig_T3) >> 14
        self.t_fine = var1 + var2
        return ((self.t_fine * 5 + 128) >> 8) / 100.0

    def get_pressure(self):
        self.get_temperature()
        raw = self.i2c.readfrom_mem(self.address, 0xF7, 3)
        adc_p = (raw[0] << 12) | (raw[1] << 4) | (raw[2] >> 4)
        var1 = self.t_fine - 128000
        var2 = var1 * var1 * self.dig_P6
        var2 = var2 + ((var1 * self.dig_P5) << 17)
        var2 = var2 + (self.dig_P4 << 35)
        var1 = ((var1 * var1 * self.dig_P3) >> 8) + ((var1 * self.dig_P2) << 12)
        var1 = (((1 << 47) + var1) * self.dig_P1) >> 33
        if var1 == 0: return 0
        p = 1048576 - adc_p
        p = (((p << 31) - var2) * 3125) // var1
        var1 = (self.dig_P9 * (p >> 13) * (p >> 13)) >> 25
        var2 = (self.dig_P8 * p) >> 19
        p = ((p + var1 + var2) >> 8) + (self.dig_P7 << 4)
        return p / 256.0