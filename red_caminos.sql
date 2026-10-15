-- ============================================
-- Red de caminos del campus
-- En SQL Shell:  \c campus   y luego   \i 'D:/campus/red_caminos.sql'
-- Se puede ejecutar aunque ya lo hayas corrido antes (no borra datos).
-- No modifica lugares, administradores ni busquedas_log.
-- ============================================

-- Puntos de la red: esquinas, curvas, cruces y entradas de lugares
CREATE TABLE IF NOT EXISTS nodos (
    id        SERIAL PRIMARY KEY,
    nombre    VARCHAR(80),
    latitud   DOUBLE PRECISION NOT NULL CHECK (latitud BETWEEN -90 AND 90),
    longitud  DOUBLE PRECISION NOT NULL CHECK (longitud BETWEEN -180 AND 180),
    -- Solo si el nodo es una ENTRADA de ese lugar. Un lugar puede tener varias entradas.
    -- NULL = punto de paso normal.
    lugar_id  INTEGER REFERENCES lugares(id) ON DELETE SET NULL
);

-- Si ya habías creado la tabla con la versión anterior (una sola entrada por lugar), esto lo corrige
ALTER TABLE nodos DROP CONSTRAINT IF EXISTS nodos_lugar_id_key;
CREATE INDEX IF NOT EXISTS idx_nodos_lugar ON nodos(lugar_id);

-- Caminos entre nodos. Se guarda siempre nodo_a < nodo_b (un camino sirve en ambos sentidos).
CREATE TABLE IF NOT EXISTS conexiones (
    id      SERIAL PRIMARY KEY,
    nodo_a  INTEGER NOT NULL REFERENCES nodos(id) ON DELETE CASCADE,
    nodo_b  INTEGER NOT NULL REFERENCES nodos(id) ON DELETE CASCADE,
    CHECK (nodo_a < nodo_b),
    UNIQUE (nodo_a, nodo_b)
);

CREATE INDEX IF NOT EXISTS idx_conexiones_a ON conexiones(nodo_a);
CREATE INDEX IF NOT EXISTS idx_conexiones_b ON conexiones(nodo_b);