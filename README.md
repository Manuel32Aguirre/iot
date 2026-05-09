# Casa Segura IoT

Proyecto local para monitoreo de humo y ambiente con tres piezas:

- `iot-humo/`: API Spring Boot con usuarios, JWT, provision de dispositivos, dashboard y stream SSE.
- `app-front/`: app Angular preparada para Capacitor/Android con login, registro, dashboard en tiempo real y provision del ESP32.
- `main.py`: firmware MicroPython para ESP32 con modo AP de configuracion, conexion WiFi y envio de lecturas al backend.

## Variables locales

El archivo `.env` en la raiz centraliza las variables sensibles del backend y de PostgreSQL.

- `APP_PORT`: puerto del backend Spring.
- `DB_URL`, `DB_USERNAME`, `DB_PASSWORD`, `DB_DRIVER`: conexion JDBC local.
- `JWT_SECRET`: firma para tokens de usuarios y dispositivos.
- `ALERT_GAS_THRESHOLD`, `ALERT_FIRE_TEMPERATURE_THRESHOLD`: umbrales de alerta.

## Levantar PostgreSQL

Desde la raiz del workspace:

```powershell
docker compose up -d postgres
```

## Levantar backend Spring

```powershell
cd iot-humo
.\mvnw.cmd spring-boot:run
```

Puntos principales del backend:

- `POST /api/auth/register`
- `POST /api/auth/login`
- `GET /api/dashboard`
- `GET /api/dashboard/stream?token=...`
- `POST /api/devices/provisioning-token`
- `POST /api/device/readings`

Swagger queda disponible en `http://TU_IP_LOCAL:8080/swagger-ui/index.html`.

## Levantar Angular

```powershell
cd app-front
npm install
npm start
```

La app te deja definir en runtime:

- URL local de la API Spring, por ejemplo `http://192.168.1.10:8080`
- URL del ESP32 en modo configuracion, por defecto `http://192.168.4.1`
- Un boton para probar si la API local responde antes de intentar registrarte

## Flujo del ESP32

1. Carga `bmp280.py` y `main.py` en el ESP32 junto con tus dependencias MicroPython (`ahtx0.py`, `urequests.py` si tu firmware no la trae).
2. Si el ESP32 no tiene configuracion WiFi, ahora seguira monitoreando por consola como antes.
3. Si quieres entrar al modo de provision, reinicialo manteniendo presionado el boton `BOOT`.
4. En ese modo levantara un AP llamado `CasaSegura-XXXXXX` con clave `CasaSeguraIoT`.
5. Mientras el telefono siga en tu WiFi de casa, entra a la app y genera el token del dispositivo desde el dashboard.
6. Luego conecta el telefono a ese AP del ESP32.
7. Desde la app manda SSID, contrasena WiFi, `deviceCode` y el endpoint de ingesta usando el token generado en el paso anterior.
8. El ESP32 reiniciara, se conectara a tu router y comenzara a enviar una lectura cada 30 segundos por defecto.

## Generar Android APK

La app ya incluye Capacitor. Si es la primera vez, ejecuta:

```powershell
cd app-front
npm install
npx cap add android
```

Luego, para cada build:

```powershell
cd app-front
npm run build:mobile
npx cap sync android
npx cap open android
```

En Android Studio:

1. Espera a que Gradle sincronice.
2. Concede permiso de notificaciones para alertas de gas/incendio.
3. Usa `Build > Build Bundle(s) / APK(s) > Build APK(s)` para generar el APK debug.
4. Si quieres release, firma la app desde `Build > Generate Signed Bundle / APK`.

## Validaciones hechas

- Backend: `mvnw test` exitoso en `iot-humo/`.
- Frontend: `npm run build` exitoso en `app-front/`.
- Firmware: sin errores de sintaxis detectados por el editor en `main.py`.