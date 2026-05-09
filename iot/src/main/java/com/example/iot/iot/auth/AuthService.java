package com.example.iot.iot.auth;

import com.example.iot.iot.user.UserAccount;
import com.example.iot.iot.user.UserAccountRepository;
import org.springframework.http.HttpStatus;
import org.springframework.security.crypto.bcrypt.BCryptPasswordEncoder;
import org.springframework.security.crypto.password.PasswordEncoder;
import org.springframework.stereotype.Service;
import org.springframework.web.server.ResponseStatusException;

@Service
public class AuthService {

    private final UserAccountRepository userAccountRepository;
    private final PasswordEncoder passwordEncoder = new BCryptPasswordEncoder();

    public AuthService(UserAccountRepository userAccountRepository) {
        this.userAccountRepository = userAccountRepository;
    }

    public AuthResponse register(RegisterRequest request) {
        String normalizedEmail = request.email().trim().toLowerCase();
        if (userAccountRepository.existsByEmailIgnoreCase(normalizedEmail)) {
            throw new ResponseStatusException(HttpStatus.CONFLICT, "Ese correo ya esta registrado.");
        }

        UserAccount userAccount = new UserAccount();
        userAccount.setEmail(normalizedEmail);
        userAccount.setPasswordHash(passwordEncoder.encode(request.password()));

        UserAccount savedUser = userAccountRepository.save(userAccount);
        return new AuthResponse(savedUser.getId(), savedUser.getEmail(), "Usuario registrado correctamente.");
    }

    public AuthResponse login(LoginRequest request) {
        UserAccount userAccount = userAccountRepository.findByEmailIgnoreCase(request.email().trim().toLowerCase())
            .orElseThrow(() -> new ResponseStatusException(HttpStatus.UNAUTHORIZED, "Credenciales invalidas."));

        if (!passwordEncoder.matches(request.password(), userAccount.getPasswordHash())) {
            throw new ResponseStatusException(HttpStatus.UNAUTHORIZED, "Credenciales invalidas.");
        }

        return new AuthResponse(userAccount.getId(), userAccount.getEmail(), "Inicio de sesion correcto.");
    }
}