-- ============================================
-- Base de datos: Campus Cajicá (PostgreSQL)
-- Ejecutar: psql -U postgres -d campus -f schema.sql
-- ============================================

DROP TABLE IF EXISTS busquedas_log;
DROP TABLE IF EXISTS administradores;
DROP TABLE IF EXISTS lugares;

CREATE TABLE lugares (
    id          SERIAL PRIMARY KEY,
    nombre      VARCHAR(120) NOT NULL,
    categoria   VARCHAR(50)  NOT NULL,   -- Aulas, Cafetería, Biblioteca, Administrativo, etc.
    descripcion TEXT,
    indicacion TEXT,
    horario     VARCHAR(120),
    latitud     DOUBLE PRECISION NOT NULL CHECK (latitud BETWEEN -90 AND 90),
    longitud    DOUBLE PRECISION NOT NULL CHECK (longitud BETWEEN -180 AND 180)
);

CREATE TABLE administradores (
    id         SERIAL PRIMARY KEY,
    nickname   VARCHAR(50) UNIQUE NOT NULL,
    llave_hash VARCHAR(255) NOT NULL      -- hash bcrypt, NUNCA la contraseña en texto plano
);

CREATE TABLE busquedas_log (
    id         SERIAL PRIMARY KEY,
    lugar_id   INTEGER NOT NULL REFERENCES lugares(id) ON DELETE CASCADE,
    fecha_hora TIMESTAMP NOT NULL DEFAULT NOW()
);

CREATE INDEX idx_busquedas_lugar ON busquedas_log(lugar_id);
CREATE INDEX idx_busquedas_fecha ON busquedas_log(fecha_hora);

-- ============================================
-- Datos de prueba (REEMPLAZA las coordenadas por las reales del campus:
-- clic derecho sobre el edificio en Google Maps y copia lat, long)
-- ============================================
INSERT INTO lugares (nombre, categoria, descripcion, indicacion, horario, latitud, longitud) VALUES
('Edificio A - Aulas',  'Aulas',          'Salones de clase del primer bloque','Entrada Bloque A izquierda' , 'L-S 6:00 - 21:00', 4.9300, -74.0300),
('Cafetería Central',   'Cafetería',      'Comidas y bebidas',                 'Salida Bloque D' , 'L-V 7:00 - 18:00', 4.9302, -74.0296),
('Biblioteca',          'Biblioteca',     'Préstamo de libros y salas de estudio', 'Entrada Bloque D' ,'L-V 7:00 - 20:00', 4.9298, -74.0302),
('Admisiones',          'Administrativo', 'Atención a aspirantes y estudiantes', 'Entrada' , 'L-V 8:00 - 17:00', 4.9305, -74.0299);

-- Consulta útil para el reporte PDF (lugares más buscados)
-- SELECT l.nombre, COUNT(b.id) AS total
-- FROM lugares l JOIN busquedas_log b ON b.lugar_id = l.id
-- GROUP BY l.nombre ORDER BY total DESC LIMIT 10;
