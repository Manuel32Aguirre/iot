package com.example.iot.iot.device;

import java.util.List;
import java.util.Optional;

import org.springframework.data.jpa.repository.JpaRepository;

public interface DeviceStateRepository extends JpaRepository<DeviceState, Long> {
    Optional<DeviceState> findByDeviceCodeIgnoreCase(String deviceCode);

    List<DeviceState> findAllByOrderByUpdatedAtDesc();
}