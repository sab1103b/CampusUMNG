-- ============================================
-- Servicios de cada lugar (salones, oficinas, laboratorios...)
-- En SQL Shell:  \c campus   y luego   \i 'D:/campus/servicios.sql'
-- No modifica la tabla lugares: solo agrega una tabla que apunta a ella.
-- ============================================

CREATE TABLE IF NOT EXISTS servicios (
    id          SERIAL PRIMARY KEY,
    lugar_id    INTEGER NOT NULL REFERENCES lugares(id) ON DELETE CASCADE,
    nombre      VARCHAR(120) NOT NULL,      -- Salón A101, Oficina de becas, Laboratorio de redes...
    categoria   VARCHAR(50),                -- Salón, Oficina, Laboratorio, Servicio...
    piso        VARCHAR(20),                -- 1, 2, Sótano...
    descripcion TEXT,
    indicacion  TEXT,                       -- cómo llegar DENTRO del lugar
    horario     VARCHAR(120)
);

CREATE INDEX IF NOT EXISTS idx_servicios_lugar ON servicios(lugar_id);

-- Ejemplos para el lugar con id 1 (solo se insertan si ese lugar existe).
-- Ejecuta esta parte UNA sola vez; bórralos luego desde el modo administrador.
INSERT INTO servicios (lugar_id, nombre, categoria, piso, descripcion, indicacion, horario)
SELECT 1, 'Salón A101', 'Salón', '1', 'Salón de clase', 'Primer pasillo a la izquierda', 'L-S 6:00 - 21:00'
WHERE EXISTS (SELECT 1 FROM lugares WHERE id = 1);

INSERT INTO servicios (lugar_id, nombre, categoria, piso, descripcion, indicacion, horario)
SELECT 1, 'Salón A202', 'Salón', '2', 'Salón de clase', 'Subir por las escaleras del fondo', 'L-S 6:00 - 21:00'
WHERE EXISTS (SELECT 1 FROM lugares WHERE id = 1);

INSERT INTO servicios (lugar_id, nombre, categoria, piso, descripcion, indicacion, horario)
SELECT 1, 'Sala de cómputo', 'Laboratorio', '1', 'Equipos para trabajo de estudiantes', 'Al final del pasillo principal', 'L-V 7:00 - 19:00'
WHERE EXISTS (SELECT 1 FROM lugares WHERE id = 1);
