# ADR 0002 — Motor estadístico en TypeScript puro

- Estado: aceptado
- Fecha: 2026-10-23

## Contexto

El producto de referencia (analizado con REA) delega el cálculo en módulos nativos (C++/Intel MKL) con
más de 43.000 exportaciones. TinyTab necesita un motor verificable, portátil, testeable y compatible con
GPLv3, y no puede reutilizar binarios ni algoritmos propietarios.

## Decisión

Implementar el motor completo en TypeScript, dentro de `src/domain`, sin dependencias estadísticas
externas:

- funciones especiales propias (Lanczos, gamma/beta incompletas, erfc vía gamma incompleta),
- álgebra lineal propia (Householder QR, Jacobi, eliminación con pivoteo),
- RNG determinista xoshiro128** con semilla explícita,
- aproximaciones documentadas y tolerancias verificadas con vectores de referencia,
- errores tipados (`Result`).

La UI puede mover el cálculo a un Web Worker sustituyendo el puerto `AnalysisRunner` sin tocar el dominio.

## Consecuencias

- Los tests de dominio se ejecutan en milisegundos y no requieren Electron.
- El rendimiento es suficiente para tamaños de muestra habituales de escritorio; los cálculos pesados
  pueden paralelizarse por columnas en un worker en etapas futuras.
- Las aproximaciones (potencia, Studentized range ausente) están documentadas como limitaciones v0.1.
