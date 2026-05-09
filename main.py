import gc
import machine
import network
import socket
import time
import ubinascii

try:
    import ujson as json
except ImportError:
    import json

try:
    import urequests
except ImportError:
    urequests = None

import ahtx0
from bmp280 import BMP280

CONFIG_FILE = "device_config.json"

I2C_SCL_PIN = 19
I2C_SDA_PIN = 18
GAS_SENSOR_PIN = 34
FAN_PIN = 22
MODE_SWITCH_PIN = 25
STATUS_LED_PIN = 23
LED_ACTIVE_HIGH = True

GAS_ALERT_THRESHOLD = 80.0
FIRE_TEMPERATURE_THRESHOLD = 58.0
SAMPLE_INTERVAL_MS = 3000
WIFI_CONNECT_TIMEOUT_MS = 15000
WIFI_RETRY_INTERVAL_MS = 5000
CONFIG_BLINK_MS = 900
CONNECTING_BLINK_MS = 220
AP_PASSWORD = "CasaSeguraIoT"
AP_IP = "192.168.4.1"
HTTP_PORT = 80

DEFAULT_CONFIG = {
    "wifi_ssid": "",
    "wifi_password": "",
    "backend_base_url": "http://192.168.0.29:8080",
    "device_code": "esp32-001",
    "device_name": "ESP32 Sala",
}

PORTAL_HTML = """<!doctype html>
<html lang="es">
<head>
  <meta charset="utf-8">
  <meta name="viewport" content="width=device-width,initial-scale=1">
  <title>Casa Segura | Configuracion</title>
  <style>
    :root {
      --bg: #f3f6fb;
      --card: #ffffff;
      --line: #d9e3ef;
      --ink: #14202b;
      --muted: #5e7082;
      --accent: #0d6efd;
      --ok: #198754;
      --warn: #fd7e14;
      font-family: Arial, sans-serif;
    }
    * { box-sizing: border-box; }
    body {
      margin: 0;
      background: linear-gradient(180deg, #eef4ff 0%, var(--bg) 100%);
      color: var(--ink);
    }
    .wrap {
      max-width: 760px;
      margin: 0 auto;
      padding: 18px;
    }
    .card {
      background: var(--card);
      border: 1px solid var(--line);
      border-radius: 18px;
      box-shadow: 0 12px 28px rgba(20, 32, 43, 0.08);
      padding: 18px;
      margin-bottom: 16px;
    }
    h1 { margin: 0 0 10px; font-size: 28px; }
    p { color: var(--muted); }
    .grid {
      display: grid;
      grid-template-columns: repeat(auto-fit, minmax(240px, 1fr));
      gap: 14px;
    }
    label {
      display: grid;
      gap: 6px;
      font-weight: 700;
      font-size: 14px;
    }
    input {
      width: 100%;
      padding: 12px 14px;
      border-radius: 12px;
      border: 1px solid var(--line);
      font-size: 15px;
    }
    button {
      border: 0;
      padding: 12px 16px;
      border-radius: 12px;
      background: var(--accent);
      color: #fff;
      font-weight: 700;
      font-size: 15px;
    }
    .status {
      padding: 12px 14px;
      border-radius: 12px;
      background: #eef6ff;
      color: var(--ink);
      border: 1px solid #cfe0fb;
      margin-bottom: 14px;
    }
    .status.ok {
      background: #e9f8ef;
      border-color: #b9e3c7;
      color: var(--ok);
    }
    .status.warn {
      background: #fff3e6;
      border-color: #ffd8ac;
      color: var(--warn);
    }
    code {
      background: #f2f5f9;
      padding: 2px 6px;
      border-radius: 8px;
    }
  </style>
</head>
<body>
  <div class="wrap">
    <section class="card">
      <h1>Casa Segura IoT</h1>
      <p>Modo configuracion activo. Mientras el switch siga en este modo, puedes cambiar los datos de conexion cuantas veces quieras.</p>
      <div id="statusBox" class="status">Esperando datos...</div>
      <p><strong>AP:</strong> <code>__AP_SSID__</code> | <strong>IP:</strong> <code>__AP_IP__</code></p>
    </section>

    <section class="card">
      <form id="configForm">
        <div class="grid">
          <label>
            SSID WiFi
            <input id="wifiSsid" name="wifiSsid" maxlength="64" placeholder="MiWiFi" value="__WIFI_SSID__" />
          </label>
          <label>
            Contrasena WiFi
            <input id="wifiPassword" name="wifiPassword" maxlength="64" type="password" placeholder="********" />
          </label>
          <label>
            URL backend Spring
            <input id="backendBaseUrl" name="backendBaseUrl" maxlength="120" placeholder="http://192.168.0.29:8080" value="__BACKEND_URL__" />
          </label>
          <label>
            Codigo del dispositivo
            <input id="deviceCode" name="deviceCode" maxlength="40" placeholder="esp32-001" value="__DEVICE_CODE__" />
          </label>
          <label>
            Nombre del dispositivo
            <input id="deviceName" name="deviceName" maxlength="60" placeholder="ESP32 Sala" value="__DEVICE_NAME__" />
          </label>
        </div>
        <div style="margin-top:16px; display:flex; gap:10px; flex-wrap:wrap;">
          <button type="submit">Guardar configuracion</button>
          <button type="button" id="refreshBtn">Actualizar estado</button>
        </div>
      </form>
    </section>
  </div>

  <script>
    const statusBox = document.getElementById('statusBox');
    const form = document.getElementById('configForm');
    const refreshBtn = document.getElementById('refreshBtn');

    function showStatus(message, tone) {
      statusBox.textContent = message;
      statusBox.className = 'status' + (tone ? ' ' + tone : '');
    }

    async function refreshStatus() {
      try {
        const response = await fetch('/status');
        const payload = await response.json();
        showStatus(
          payload.mode === 'config'
            ? 'Modo configuracion activo. Configuracion guardada: ' + (payload.hasConfig ? 'si' : 'no')
            : 'Modo operacion activo. Mueve el switch a configuracion para editar.',
          payload.mode === 'config' ? 'ok' : 'warn'
        );
      } catch (error) {
        showStatus('No se pudo leer el estado del ESP32.', 'warn');
      }
    }

    form.addEventListener('submit', async (event) => {
      event.preventDefault();
      showStatus('Guardando configuracion...', '');
      const payload = {
        wifi_ssid: document.getElementById('wifiSsid').value.trim(),
        wifi_password: document.getElementById('wifiPassword').value,
        backend_base_url: document.getElementById('backendBaseUrl').value.trim(),
        device_code: document.getElementById('deviceCode').value.trim(),
        device_name: document.getElementById('deviceName').value.trim(),
      };

      try {
        const response = await fetch('/configure', {
          method: 'POST',
          headers: { 'Content-Type': 'application/json' },
          body: JSON.stringify(payload),
        });
        const result = await response.json();
        if (!response.ok) {
          throw new Error(result.message || 'No se pudo guardar.');
        }
        document.getElementById('wifiPassword').value = '';
        showStatus(result.message || 'Configuracion guardada.', 'ok');
      } catch (error) {
        showStatus(error.message || 'Fallo de configuracion.', 'warn');
      }
    });

    refreshBtn.addEventListener('click', refreshStatus);
    refreshStatus();
    setInterval(refreshStatus, 4000);
  </script>
</body>
</html>
"""


def ap_ssid():
    return "CasaSegura-{}".format(ubinascii.hexlify(machine.unique_id()).decode()[-6:])


def load_config():
    try:
        with open(CONFIG_FILE, "r") as config_file:
            data = json.loads(config_file.read())
            if isinstance(data, dict):
                merged = DEFAULT_CONFIG.copy()
                merged.update(data)
                return merged
    except Exception:
        pass
    return DEFAULT_CONFIG.copy()


def save_config(config):
    with open(CONFIG_FILE, "w") as config_file:
        config_file.write(json.dumps(config))


def switch_in_config_mode():
    switch = machine.Pin(MODE_SWITCH_PIN, machine.Pin.IN, machine.Pin.PULL_UP)
    time.sleep_ms(20)
    return switch.value() == 0


def configure_led():
    return machine.Pin(STATUS_LED_PIN, machine.Pin.OUT)


def set_led(led_pin, on):
    led_pin.value(1 if (on == LED_ACTIVE_HIGH) else 0)


def html_response(body):
    encoded = body.encode()
    headers = [
        "HTTP/1.1 200 OK",
        "Content-Type: text/html; charset=utf-8",
        "Content-Length: {}".format(len(encoded)),
        "Connection: close",
        "",
        "",
    ]
    return "\r\n".join(headers).encode() + encoded


def json_response(status_code, payload):
    status_map = {
        200: "OK",
        201: "Created",
        400: "Bad Request",
        404: "Not Found",
        405: "Method Not Allowed",
    }
    body = json.dumps(payload).encode()
    headers = [
        "HTTP/1.1 {} {}".format(status_code, status_map.get(status_code, "OK")),
        "Content-Type: application/json",
        "Access-Control-Allow-Origin: *",
        "Access-Control-Allow-Methods: GET, POST, OPTIONS",
        "Access-Control-Allow-Headers: Content-Type",
        "Cache-Control: no-store",
        "Content-Length: {}".format(len(body)),
        "Connection: close",
        "",
        "",
    ]
    return "\r\n".join(headers).encode() + body


def safe_text(value):
    text = str(value or "")
    return (
        text.replace("&", "&amp;")
        .replace("<", "&lt;")
        .replace(">", "&gt;")
        .replace('"', "&quot;")
    )


def build_portal_html(config):
    return (
        PORTAL_HTML.replace("__AP_SSID__", safe_text(ap_ssid()))
        .replace("__AP_IP__", safe_text(AP_IP))
        .replace("__WIFI_SSID__", safe_text(config.get("wifi_ssid", "")))
        .replace("__BACKEND_URL__", safe_text(config.get("backend_base_url", "")))
        .replace("__DEVICE_CODE__", safe_text(config.get("device_code", "")))
        .replace("__DEVICE_NAME__", safe_text(config.get("device_name", "")))
    )


def parse_config_payload(payload):
    config = DEFAULT_CONFIG.copy()
    config.update(load_config())
    config["wifi_ssid"] = str(payload.get("wifi_ssid", "")).strip()
    config["wifi_password"] = str(payload.get("wifi_password", ""))
    config["backend_base_url"] = str(payload.get("backend_base_url", "")).strip().rstrip("/")
    config["device_code"] = str(payload.get("device_code", "")).strip().lower()
    config["device_name"] = str(payload.get("device_name", "")).strip()

    required = ["wifi_ssid", "backend_base_url", "device_code", "device_name"]
    missing = [field for field in required if not config[field]]
    if missing:
        raise ValueError("Faltan campos: {}".format(", ".join(missing)))
    return config


def configure_sensor_suite():
    i2c = machine.I2C(0, scl=machine.Pin(I2C_SCL_PIN), sda=machine.Pin(I2C_SDA_PIN))
    gas_sensor = machine.ADC(machine.Pin(GAS_SENSOR_PIN))
    gas_sensor.atten(machine.ADC.ATTN_11DB)
    return {
        "gas_sensor": gas_sensor,
        "sensor_aht": ahtx0.AHT20(i2c),
        "sensor_bmp": BMP280(i2c, address=0x77),
    }


def controlar_ventilador(estado):
    if estado:
        machine.Pin(FAN_PIN, machine.Pin.OUT, value=0)
    else:
        machine.Pin(FAN_PIN, machine.Pin.IN)


def read_snapshot(sensors, sample_number, wifi_connected, wifi_ip):
    temperature = round(sensors["sensor_aht"].temperature, 2)
    humidity = round(sensors["sensor_aht"].relative_humidity, 2)
    pressure = round(sensors["sensor_bmp"].get_pressure() / 100.0, 2)
    gas_level = round((sensors["gas_sensor"].read() / 4095) * 100, 2)
    gas_alert = gas_level >= GAS_ALERT_THRESHOLD
    fire_alert = temperature >= FIRE_TEMPERATURE_THRESHOLD
    relay_on = gas_alert or fire_alert
    controlar_ventilador(relay_on)
    return {
        "sampleNumber": sample_number,
        "temperatureC": temperature,
        "humidity": humidity,
        "pressureHpa": pressure,
        "gasLevel": gas_level,
        "gasAlert": gas_alert,
        "fireAlert": fire_alert,
        "relayOn": relay_on,
        "wifiConnected": wifi_connected,
        "wifiIp": wifi_ip,
        "timestampMs": time.ticks_ms(),
    }


def print_snapshot(snapshot):
    print("-" * 60)
    print(
        "ENTORNO | T: {0:.1f}C | H: {1:.1f}% | P: {2:.1f} hPa".format(
            snapshot["temperatureC"], snapshot["humidity"], snapshot["pressureHpa"]
        )
    )
    print(
        "PELIGRO | Gas: {0:.1f}% | Fuego: {1}".format(
            snapshot["gasLevel"], "SI" if snapshot["fireAlert"] else "NO"
        )
    )
    print(
        "ESTADO  | Ventilador: {0} | WiFi: {1}".format(
            "ENCENDIDO" if snapshot["relayOn"] else "APAGADO",
            snapshot["wifiIp"] if snapshot["wifiConnected"] else "SIN CONEXION",
        )
    )


def start_access_point():
    access_point = network.WLAN(network.AP_IF)
    access_point.active(True)
    access_point.config(essid=ap_ssid(), password=AP_PASSWORD)
    access_point.ifconfig((AP_IP, "255.255.255.0", AP_IP, "8.8.8.8"))
    return access_point


def stop_access_point(access_point):
    if access_point is not None:
        access_point.active(False)


def ensure_station():
    station = network.WLAN(network.STA_IF)
    station.active(True)
    return station


def stop_station(station):
    if station is not None:
        try:
            if station.isconnected():
                station.disconnect()
        except Exception:
            pass
        station.active(False)


def maybe_post_reading(config, snapshot):
    if urequests is None:
        return "Sin urequests, no se envio a backend."

    ingest_url = "{}/api/device/readings".format(config["backend_base_url"])
    payload = {
        "deviceCode": config["device_code"],
        "deviceName": config["device_name"],
        "temperatureC": snapshot["temperatureC"],
        "humidity": snapshot["humidity"],
        "pressureHpa": snapshot["pressureHpa"],
        "gasLevel": snapshot["gasLevel"],
        "gasAlert": snapshot["gasAlert"],
        "fireAlert": snapshot["fireAlert"],
        "relayOn": snapshot["relayOn"],
    }
    response = None
    try:
        response = urequests.post(
            ingest_url,
            data=json.dumps(payload),
            headers={"Content-Type": "application/json"},
        )
        return "POST {} -> {}".format(ingest_url, response.status_code)
    except Exception as error:
        return "Error enviando lectura: {}".format(error)
    finally:
        if response is not None:
            response.close()
        gc.collect()


def handle_http_request(client_socket, access_point):
    client_file = client_socket.makefile("rwb", 0)
    try:
        request_line = client_file.readline()
        if not request_line:
            return

        parts = request_line.decode().strip().split()
        if len(parts) < 2:
            client_socket.send(json_response(400, {"message": "Peticion invalida"}))
            return

        method = parts[0]
        path = parts[1]
        content_length = 0

        while True:
            header_line = client_file.readline()
            if not header_line or header_line == b"\r\n":
                break
            header_text = header_line.decode().strip()
            if header_text.lower().startswith("content-length:"):
                content_length = int(header_text.split(":", 1)[1].strip())

        body = client_file.read(content_length) if content_length else b""

        if method == "OPTIONS":
            client_socket.send(json_response(200, {}))
            return

        if method == "GET" and (path == "/" or path == "/index.html"):
            client_socket.send(html_response(build_portal_html(load_config())))
            return

        if method == "GET" and path == "/status":
            config = load_config()
            client_socket.send(
                json_response(
                    200,
                    {
                        "mode": "config" if switch_in_config_mode() else "operation",
                        "hasConfig": bool(config.get("wifi_ssid") and config.get("backend_base_url")),
                        "apSsid": ap_ssid(),
                        "apIp": access_point.ifconfig()[0],
                    },
                )
            )
            return

        if method == "POST" and path == "/configure":
            try:
                payload = json.loads(body.decode() or "{}")
                config = parse_config_payload(payload)
                save_config(config)
                client_socket.send(
                    json_response(
                        201,
                        {
                            "message": "Configuracion guardada. Mueve el switch a modo operacion para conectar el ESP32.",
                            "deviceCode": config["device_code"],
                        },
                    )
                )
            except ValueError as error:
                client_socket.send(json_response(400, {"message": str(error)}))
            return

        client_socket.send(json_response(404, {"message": "Ruta no encontrada"}))
    finally:
        try:
            client_file.close()
        except Exception:
            pass


def main():
    print(">>> CASA SEGURA IOT: ESP32 configuracion + backend Spring <<<")

    led_pin = configure_led()
    set_led(led_pin, False)
    sensors = configure_sensor_suite()

    access_point = None
    config_server = None
    station = ensure_station()
    current_mode = None
    wifi_connect_started_at = None
    last_wifi_retry_at = 0
    led_on = False
    last_led_toggle_at = time.ticks_ms()
    last_sample_at = 0
    sample_number = 0

    while True:
        config_mode = switch_in_config_mode()
        mode = "config" if config_mode else "operation"

        if mode != current_mode:
            current_mode = mode
            if mode == "config":
                print("Switch en CONFIGURACION. AP activo y portal listo para Meta Quest.")
                stop_station(station)
                station = ensure_station()
                stop_station(station)
                access_point = start_access_point()
                if config_server is None:
                    config_server = socket.socket()
                    config_server.setsockopt(socket.SOL_SOCKET, socket.SO_REUSEADDR, 1)
                    config_server.bind(("0.0.0.0", HTTP_PORT))
                    config_server.listen(2)
                    config_server.settimeout(0.2)
                print("AP listo: {}".format(access_point.config("essid")))
                print("Clave AP: {}".format(AP_PASSWORD))
                print("Abre http://{}/ desde el navegador del Quest".format(access_point.ifconfig()[0]))
            else:
                print("Switch en OPERACION. Cerrando AP e intentando conectar a WiFi...")
                if config_server is not None:
                    config_server.close()
                    config_server = None
                stop_access_point(access_point)
                access_point = None
                station = ensure_station()
                wifi_connect_started_at = None
                last_wifi_retry_at = 0

        blink_period = CONFIG_BLINK_MS if current_mode == "config" else CONNECTING_BLINK_MS
        wifi_connected = False
        wifi_ip = ""

        if current_mode == "config":
            if time.ticks_diff(time.ticks_ms(), last_led_toggle_at) >= blink_period:
                led_on = not led_on
                set_led(led_pin, led_on)
                last_led_toggle_at = time.ticks_ms()

            if config_server is not None:
                try:
                    client_socket, _ = config_server.accept()
                except OSError:
                    client_socket = None
                if client_socket is not None:
                    try:
                        handle_http_request(client_socket, access_point)
                    finally:
                        client_socket.close()

        else:
            config = load_config()
            ready_for_operation = bool(config.get("wifi_ssid") and config.get("backend_base_url"))
            if not ready_for_operation:
                if time.ticks_diff(time.ticks_ms(), last_led_toggle_at) >= CONNECTING_BLINK_MS:
                    led_on = not led_on
                    set_led(led_pin, led_on)
                    last_led_toggle_at = time.ticks_ms()
                print("No hay configuracion guardada. Mueve el switch a modo configuracion para capturar WiFi.")
                time.sleep_ms(800)
                continue

            if not station.isconnected():
                if (
                    wifi_connect_started_at is None
                    or time.ticks_diff(time.ticks_ms(), wifi_connect_started_at) > WIFI_CONNECT_TIMEOUT_MS
                ):
                    if time.ticks_diff(time.ticks_ms(), last_wifi_retry_at) >= WIFI_RETRY_INTERVAL_MS:
                        try:
                            station.disconnect()
                        except Exception:
                            pass
                        print("Conectando a WiFi '{}'...".format(config["wifi_ssid"]))
                        station.connect(config["wifi_ssid"], config["wifi_password"])
                        wifi_connect_started_at = time.ticks_ms()
                        last_wifi_retry_at = time.ticks_ms()

                if time.ticks_diff(time.ticks_ms(), last_led_toggle_at) >= CONNECTING_BLINK_MS:
                    led_on = not led_on
                    set_led(led_pin, led_on)
                    last_led_toggle_at = time.ticks_ms()
            else:
                wifi_connected = True
                wifi_ip = station.ifconfig()[0]
                set_led(led_pin, True)
                led_on = True
                wifi_connect_started_at = None

        if time.ticks_diff(time.ticks_ms(), last_sample_at) >= SAMPLE_INTERVAL_MS:
            sample_number += 1
            if current_mode == "operation" and station.isconnected():
                wifi_connected = True
                wifi_ip = station.ifconfig()[0]
            snapshot = read_snapshot(sensors, sample_number, wifi_connected, wifi_ip)
            print_snapshot(snapshot)

            if current_mode == "operation" and station.isconnected():
                result = maybe_post_reading(load_config(), snapshot)
                print(result)

            last_sample_at = time.ticks_ms()

        time.sleep_ms(80)


if __name__ == "__main__":
    main()