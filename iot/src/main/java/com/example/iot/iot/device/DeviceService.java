package com.example.iot.iot.device;

import java.time.Duration;
import java.time.Instant;
import java.util.List;

import org.springframework.http.HttpStatus;
import org.springframework.stereotype.Service;
import org.springframework.web.server.ResponseStatusException;

@Service
public class DeviceService {

    private static final Duration DEVICE_OFFLINE_AFTER = Duration.ofSeconds(90);

    private final DeviceStateRepository deviceStateRepository;

    public DeviceService(DeviceStateRepository deviceStateRepository) {
        this.deviceStateRepository = deviceStateRepository;
    }

    public DeviceReadingResponse ingestReading(DeviceReadingRequest request) {
        String deviceCode = normalizeDeviceCode(request.deviceCode());
        DeviceState state = deviceStateRepository.findByDeviceCodeIgnoreCase(deviceCode)
            .orElseGet(DeviceState::new);

        state.setDeviceCode(deviceCode);
        state.setDeviceName(request.deviceName().trim());
        state.setTemperatureC(request.temperatureC());
        state.setHumidity(request.humidity());
        state.setPressureHpa(request.pressureHpa());
        state.setGasLevel(request.gasLevel());
        state.setGasAlert(request.gasAlert());
        state.setFireAlert(request.fireAlert());
        state.setRelayOn(request.relayOn());

        DeviceState saved = deviceStateRepository.save(state);
        return new DeviceReadingResponse(saved.getDeviceCode(), saved.isFanOverrideEnabled(), saved.getUpdatedAt());
    }

    public List<DashboardResponse> getAllReadings() {
        return deviceStateRepository.findAllByOrderByUpdatedAtDesc().stream()
            .map(this::toDashboardResponse)
            .toList();
    }

    public DashboardResponse getDashboard(String requestedDeviceCode) {
        DeviceState state = resolveDevice(requestedDeviceCode);
        return toDashboardResponse(state);
    }

    public FanOverrideResponse setFanOverride(FanOverrideRequest request) {
        DeviceState state = resolveDevice(request.deviceCode());
        state.setFanOverrideEnabled(request.enabled());
        DeviceState saved = deviceStateRepository.save(state);
        return new FanOverrideResponse(
            saved.getDeviceCode(),
            saved.isFanOverrideEnabled(),
            saved.getUpdatedAt(),
            saved.isFanOverrideEnabled() ? "Ventilador manual encendido." : "Ventilador manual apagado."
        );
    }

    public DeviceReadingResponse getFanOverride(String requestedDeviceCode) {
        DeviceState state = resolveDevice(requestedDeviceCode);
        return new DeviceReadingResponse(state.getDeviceCode(), state.isFanOverrideEnabled(), state.getUpdatedAt());
    }

    private DeviceState resolveDevice(String requestedDeviceCode) {
        if (requestedDeviceCode != null && !requestedDeviceCode.isBlank()) {
            String deviceCode = normalizeDeviceCode(requestedDeviceCode);
            return deviceStateRepository.findByDeviceCodeIgnoreCase(deviceCode)
                .orElseThrow(() -> new ResponseStatusException(HttpStatus.NOT_FOUND, "Dispositivo no encontrado."));
        }

        return deviceStateRepository.findAll().stream().findFirst()
            .orElseThrow(() -> new ResponseStatusException(HttpStatus.NOT_FOUND, "No hay lecturas disponibles."));
    }

    private String normalizeDeviceCode(String deviceCode) {
        return deviceCode.trim().toLowerCase();
    }

    private DashboardResponse toDashboardResponse(DeviceState state) {
        boolean online = Duration.between(state.getUpdatedAt(), Instant.now()).compareTo(DEVICE_OFFLINE_AFTER) <= 0;
        return new DashboardResponse(
            state.getDeviceCode(),
            state.getDeviceName(),
            state.getTemperatureC(),
            state.getHumidity(),
            state.getPressureHpa(),
            state.getGasLevel(),
            state.isGasAlert(),
            state.isFireAlert(),
            state.isRelayOn(),
            state.isFanOverrideEnabled(),
            online,
            online ? "online" : "offline",
            state.getUpdatedAt()
        );
    }
}