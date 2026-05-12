Archivos que debes subir al ESP32 para este firmware:

- `main.py`
- `bmp280.py`
- `ahtx0.py`

Opcional:

- `urequests.py` solo si quieres que el ESP32 envie lecturas al backend Spring. Si no esta, el firmware sigue corriendo pero no hace el POST.

Notas:

- `device_config.json` no hace falta subirlo: el firmware lo crea cuando guardas la configuracion desde el portal web.
- Los modulos `gc`, `machine`, `network`, `socket`, `time`, `ubinascii`, `json` o `ujson`, y `struct` vienen con MicroPython.
- El sensor AHT20 usa la direccion I2C `0x38` en `ahtx0.py`.
- El BMP280 en este proyecto usa la direccion I2C `0x77` desde `main.py`.