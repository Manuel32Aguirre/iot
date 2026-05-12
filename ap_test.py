import network
import time


AP_SSID = "ESP32-Test-AP"
AP_PASSWORD = "12345678"


def main():
    print("Iniciando prueba minima de Access Point...")
    ap = network.WLAN(network.AP_IF)
    ap.active(False)
    time.sleep_ms(200)

    ap.active(True)
    ap.config(essid=AP_SSID, password=AP_PASSWORD)

    print("AP activo:", ap.active())
    print("SSID:", AP_SSID)
    print("IP:", ap.ifconfig()[0])

    while True:
        print("Prueba AP viva")
        time.sleep(2)


if __name__ == "__main__":
    main()