package com.example.iot.iot.auth;

import jakarta.validation.constraints.Email;
import jakarta.validation.constraints.NotBlank;
import jakarta.validation.constraints.Size;

record RegisterRequest(
    @NotBlank @Email String email,
    @NotBlank @Size(min = 8, max = 120) String password
) {
}

record LoginRequest(
    @NotBlank @Email String email,
    @NotBlank String password
) {
}

record AuthResponse(
    Long id,
    String email,
    String message
) {
}