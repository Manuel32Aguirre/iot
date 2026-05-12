package com.example.iot.iot.device;

import java.util.List;

import org.springframework.http.HttpStatus;
import org.springframework.web.bind.annotation.GetMapping;
import org.springframework.web.bind.annotation.PostMapping;
import org.springframework.web.bind.annotation.RequestBody;
import org.springframework.web.bind.annotation.RequestMapping;
import org.springframework.web.bind.annotation.RequestParam;
import org.springframework.web.bind.annotation.ResponseStatus;
import org.springframework.web.bind.annotation.RestController;

import jakarta.validation.Valid;

@RestController
@RequestMapping("/api")
public class DeviceController {

    private final DeviceService deviceService;

    public DeviceController(DeviceService deviceService) {
        this.deviceService = deviceService;
    }

    @PostMapping("/device/readings")
    @ResponseStatus(HttpStatus.CREATED)
    public DeviceReadingResponse ingestReading(@Valid @RequestBody DeviceReadingRequest request) {
        return deviceService.ingestReading(request);
    }

    @GetMapping("/device/readings")
    public List<DashboardResponse> getAllReadings() {
        return deviceService.getAllReadings();
    }

    @GetMapping("/dashboard")
    public DashboardResponse getDashboard(@RequestParam(required = false) String deviceCode) {
        return deviceService.getDashboard(deviceCode);
    }

    @PostMapping("/device/fan")
    public FanOverrideResponse setFanOverride(@Valid @RequestBody FanOverrideRequest request) {
        return deviceService.setFanOverride(request);
    }

    @GetMapping("/device/fan")
    public DeviceReadingResponse getFanOverride(@RequestParam(required = false) String deviceCode) {
        return deviceService.getFanOverride(deviceCode);
    }
}