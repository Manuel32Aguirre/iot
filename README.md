# Casa Segura IoT

Proyecto local para monitoreo ambiental y control de ventilacion con tres piezas principales:

- `main.py`: firmware MicroPython para ESP32 con sensores por I2C, lectura de gas por ADC, modo AP de configuracion y envio de lecturas al backend.
- `iot/`: API Spring Boot con registro y login basicos, ingesta de telemetria, consulta de dashboard, override manual del ventilador y descubrimiento LAN por UDP.
- `iot_metaquest3/iot/`: proyecto Unity para Meta Quest 3 que descubre el backend en la red local, consulta telemetria y permite controlar el ventilador.

## Estructura del repositorio

- `main.py`, `ahtx0.py`, `bmp280.py`: firmware y drivers MicroPython.
- `docker-compose.yml`: servicio local de PostgreSQL.
- `iot/`: backend Java con Spring Boot y Maven.
- `iot_metaquest3/iot/`: cliente Unity para Meta Quest 3.
- `ESP32_UPLOADS.md`: archivos que debes copiar al ESP32.

## Variables locales

El archivo `.env` en la raiz centraliza la configuracion sensible del backend y de PostgreSQL.

- `APP_PORT`: puerto del backend Spring.
- `DB_URL`, `DB_USERNAME`, `DB_PASSWORD`, `DB_DRIVER`: conexion JDBC local a PostgreSQL.

## Levantar PostgreSQL

Desde la raiz del workspace:

```powershell
docker compose up -d postgres
```

## Levantar backend Spring

```powershell
cd iot
.\mvnw.cmd spring-boot:run
```

Swagger queda disponible en `http://TU_IP_LOCAL:8080/swagger-ui.html`.

### Endpoints principales

- `POST /api/auth/register`
- `POST /api/auth/login`
- `GET /api/device/readings`
- `GET /api/dashboard?deviceCode=...`
- `POST /api/device/readings`
- `POST /api/device/fan`
- `GET /api/device/fan?deviceCode=...`

## Firmware del ESP32

1. Carga `main.py`, `bmp280.py` y `ahtx0.py` en el ESP32.
2. Si tu firmware MicroPython no incluye `urequests`, sube tambien `urequests.py`.
3. El switch conectado a `MODE_SWITCH_PIN` define el modo de trabajo:
	- configuracion: levanta un AP `CasaSegura-XXXXXX` con clave `CasaSeguraIoT`
	- operacion: se conecta a la red WiFi, descubre el backend en la LAN y envia lecturas
4. En modo configuracion, abre `http://192.168.4.1/` desde el navegador del Quest, un telefono o una laptop para guardar el SSID y la contrasena del WiFi.
5. En modo operacion, el ESP32 intenta descubrir automaticamente el backend por UDP en el puerto `8266`.
6. Cuando encuentra el backend, envia lecturas JSON a `POST /api/device/readings`.
7. El intervalo de muestreo actual es de `500 ms`.

## Proyecto Meta Quest 3

- Abre `iot_metaquest3/iot/` con Unity `6000.2.8f1`.
- El runtime principal esta en `Assets/Scripts/IotQuestRuntime.cs`.
- La app descubre el backend por UDP, consulta `GET /api/device/readings` y permite enviar overrides a `POST /api/device/fan`.
- El APK de pruebas que existe en el repo local es `iot_metaquest3/iot/apk1.apk`, pero no debe versionarse.

## Validaciones hechas

- Backend: `.\mvnw.cmd test` exitoso en `iot/`.
- Firmware: sin errores de sintaxis reportados por el editor en `main.py`.