package com.example.iot.iot.device;

import java.time.Instant;

import jakarta.validation.constraints.NotBlank;
import jakarta.validation.constraints.NotNull;
import jakarta.validation.constraints.Size;

record DeviceReadingRequest(
    @NotBlank @Size(max = 64) String deviceCode,
    @NotBlank @Size(max = 120) String deviceName,
    @NotNull Double temperatureC,
    @NotNull Double humidity,
    @NotNull Double pressureHpa,
    @NotNull Double gasLevel,
    @NotNull Boolean gasAlert,
    @NotNull Boolean fireAlert,
    @NotNull Boolean relayOn
) {
}

record FanOverrideRequest(
    @NotBlank @Size(max = 64) String deviceCode,
    @NotNull Boolean enabled
) {
}

record DeviceReadingResponse(
    String deviceCode,
    boolean fanOverrideEnabled,
    Instant updatedAt
) {
}

record DashboardResponse(
    String deviceCode,
    String deviceName,
    double temperatureC,
    double humidity,
    double pressureHpa,
    double gasLevel,
    boolean gasAlert,
    boolean fireAlert,
    boolean relayOn,
    boolean fanOverrideEnabled,
    boolean wifiConnected,
    String status,
    Instant updatedAt
) {
}

record FanOverrideResponse(
    String deviceCode,
    boolean enabled,
    Instant updatedAt,
    String message
) {
}