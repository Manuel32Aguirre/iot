package com.example.iot.iot.device;

import java.time.Instant;

import jakarta.persistence.Column;
import jakarta.persistence.Entity;
import jakarta.persistence.GeneratedValue;
import jakarta.persistence.GenerationType;
import jakarta.persistence.Id;
import jakarta.persistence.PrePersist;
import jakarta.persistence.PreUpdate;
import jakarta.persistence.Table;

@Entity
@Table(name = "device_states")
public class DeviceState {

    @Id
    @GeneratedValue(strategy = GenerationType.IDENTITY)
    private Long id;

    @Column(nullable = false, unique = true, length = 64)
    private String deviceCode;

    @Column(nullable = false, length = 120)
    private String deviceName;

    @Column(nullable = false)
    private double temperatureC;

    @Column(nullable = false)
    private double humidity;

    @Column(nullable = false)
    private double pressureHpa;

    @Column(nullable = false)
    private double gasLevel;

    @Column(nullable = false)
    private boolean gasAlert;

    @Column(nullable = false)
    private boolean fireAlert;

    @Column(nullable = false)
    private boolean relayOn;

    @Column(nullable = false)
    private boolean fanOverrideEnabled;

    @Column(nullable = false)
    private Instant createdAt;

    @Column(nullable = false)
    private Instant updatedAt;

    @PrePersist
    void onCreate() {
        Instant now = Instant.now();
        createdAt = now;
        updatedAt = now;
    }

    @PreUpdate
    void onUpdate() {
        updatedAt = Instant.now();
    }

    public Long getId() {
        return id;
    }

    public String getDeviceCode() {
        return deviceCode;
    }

    public void setDeviceCode(String deviceCode) {
        this.deviceCode = deviceCode;
    }

    public String getDeviceName() {
        return deviceName;
    }

    public void setDeviceName(String deviceName) {
        this.deviceName = deviceName;
    }

    public double getTemperatureC() {
        return temperatureC;
    }

    public void setTemperatureC(double temperatureC) {
        this.temperatureC = temperatureC;
    }

    public double getHumidity() {
        return humidity;
    }

    public void setHumidity(double humidity) {
        this.humidity = humidity;
    }

    public double getPressureHpa() {
        return pressureHpa;
    }

    public void setPressureHpa(double pressureHpa) {
        this.pressureHpa = pressureHpa;
    }

    public double getGasLevel() {
        return gasLevel;
    }

    public void setGasLevel(double gasLevel) {
        this.gasLevel = gasLevel;
    }

    public boolean isGasAlert() {
        return gasAlert;
    }

    public void setGasAlert(boolean gasAlert) {
        this.gasAlert = gasAlert;
    }

    public boolean isFireAlert() {
        return fireAlert;
    }

    public void setFireAlert(boolean fireAlert) {
        this.fireAlert = fireAlert;
    }

    public boolean isRelayOn() {
        return relayOn;
    }

    public void setRelayOn(boolean relayOn) {
        this.relayOn = relayOn;
    }

    public boolean isFanOverrideEnabled() {
        return fanOverrideEnabled;
    }

    public void setFanOverrideEnabled(boolean fanOverrideEnabled) {
        this.fanOverrideEnabled = fanOverrideEnabled;
    }

    public Instant getCreatedAt() {
        return createdAt;
    }

    public Instant getUpdatedAt() {
        return updatedAt;
    }
}