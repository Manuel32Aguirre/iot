package com.example.iot.iot.discovery;

import java.io.IOException;
import java.net.DatagramPacket;
import java.net.DatagramSocket;
import java.net.SocketException;
import java.nio.charset.StandardCharsets;

import org.springframework.beans.factory.annotation.Value;
import org.springframework.stereotype.Component;

import jakarta.annotation.PreDestroy;

@Component
public class LanDiscoveryResponder {

    private static final String DISCOVERY_REQUEST = "CASA_SEGURA_DISCOVER";
    private static final String RESPONSE_PREFIX = "IOT_BACKEND ";

    private final int discoveryPort;
    private final int serverPort;

    private DatagramSocket socket;
    private Thread workerThread;
    private volatile boolean running;

    public LanDiscoveryResponder(
        @Value("${iot.discovery.port:8266}") int discoveryPort,
        @Value("${server.port:8080}") int serverPort
    ) {
        this.discoveryPort = discoveryPort;
        this.serverPort = serverPort;
        start();
    }

    private void start() {
        try {
            socket = new DatagramSocket(discoveryPort);
            socket.setBroadcast(true);
        } catch (SocketException exception) {
            throw new IllegalStateException("No se pudo iniciar descubrimiento LAN en UDP " + discoveryPort, exception);
        }

        running = true;
        workerThread = new Thread(this::listenLoop, "iot-discovery-responder");
        workerThread.setDaemon(true);
        workerThread.start();
    }

    private void listenLoop() {
        byte[] buffer = new byte[256];
        while (running) {
            try {
                DatagramPacket packet = new DatagramPacket(buffer, buffer.length);
                socket.receive(packet);
                String payload = new String(packet.getData(), packet.getOffset(), packet.getLength(), StandardCharsets.UTF_8).trim();
                if (!DISCOVERY_REQUEST.equals(payload)) {
                    continue;
                }

                String responsePayload = RESPONSE_PREFIX + serverPort;
                byte[] responseBytes = responsePayload.getBytes(StandardCharsets.UTF_8);
                DatagramPacket response = new DatagramPacket(
                    responseBytes,
                    responseBytes.length,
                    packet.getAddress(),
                    packet.getPort()
                );
                socket.send(response);
            } catch (IOException ignored) {
                if (!running) {
                    return;
                }
            }
        }
    }

    @PreDestroy
    @SuppressWarnings("unused")
    void stop() {
        running = false;
        if (socket != null && !socket.isClosed()) {
            socket.close();
        }
        if (workerThread != null) {
            workerThread.interrupt();
        }
    }
}