# ADR 0004 — Formatos `.ttab`, `.ttw` y CSV

- Estado: aceptado
- Fecha: 2026-10-23

## Contexto

El usuario debe poder guardar proyectos, hojas e intercambiar datos con otras herramientas.

## Decisión

- `.ttab`: proyecto completo en JSON con `format: "tinytab-project"` y `version: 1`. Contiene hojas,
  sesión y gráficos serializables.
- `.ttw`: hoja de trabajo en texto con cabecera `# TinyTab Worksheet v1` (en v0.1 se exporta como CSV con
  metadatos en el nombre; el formato binario queda reservado).
- CSV: importación con detección de delimitador (`,`/`;`/tab), encabezados e inferencia de tipos;
  exportación UTF-8 con comillas cuando hace falta.
- Sesión: texto plano dentro del proyecto y exportación HTML autocontenida.

La escritura es atómica en el proceso principal (tmp + rename) para evitar corrupción. No se escribe en
ninguna ruta que el usuario no haya elegido mediante el diálogo nativo.

## Consecuencias

- Los proyectos son legibles y versionables; un cambio de esquema requiere subir `version` y migrar.
- El formato no es compatible con archivos de Minitab (`.mpx`/`.mtw`), por diseño: no se reutilizan
  formatos propietarios.
