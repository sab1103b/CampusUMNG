"""
MIGRACIÓN DE UNA SOLA VEZ: PostgreSQL  ->  base local de Unity (SQLite).

Copia lugares, servicios, red de caminos y el historial de búsquedas que tienes hoy
en PostgreSQL al archivo campus/Assets/BaseDatos/semilla.sql, conservando los ids.

Uso (con PostgreSQL encendido, una última vez):
    cd D:\\campus\\backend
    venv\\Scripts\\python exportar_semilla.py

Después, en Unity:
    Campus > Base de datos > Reconstruir desde semilla.sql
    Campus > Base de datos > Administradores...   (las llaves bcrypt no se pueden migrar)
"""
from datetime import datetime
from pathlib import Path

from database import get_conn

DESTINO = Path(__file__).resolve().parent.parent / "campus" / "Assets" / "BaseDatos" / "semilla.sql"

TABLAS = [
    ("lugares", ["id", "nombre", "categoria", "descripcion", "indicacion", "horario", "latitud", "longitud"]),
    ("servicios", ["id", "lugar_id", "nombre", "categoria", "piso", "descripcion", "indicacion", "horario"]),
    ("nodos", ["id", "nombre", "latitud", "longitud", "lugar_id"]),
    ("conexiones", ["id", "nodo_a", "nodo_b"]),
    ("busquedas_log", ["id", "lugar_id", "fecha_hora"]),
]


def reparar_tildes(t: str) -> str:
    """Corrige textos guardados con doble codificación (p. ej. 'SalÃ³n' -> 'Salón'),
    que aparecen cuando un .sql UTF-8 se ejecutó en psql con codificación de Windows."""
    if "Ã" not in t and "Â" not in t:
        return t
    for codificacion in ("cp1252", "latin-1"):
        try:
            return t.encode(codificacion).decode("utf-8")
        except (UnicodeEncodeError, UnicodeDecodeError):
            continue
    return t


def literal(v) -> str:
    if v is None:
        return "NULL"
    if isinstance(v, bool):
        return "1" if v else "0"
    if isinstance(v, (int, float)):
        return repr(v)
    if isinstance(v, datetime):
        v = v.strftime("%Y-%m-%d %H:%M:%S")
    return "'" + reparar_tildes(str(v)).replace("'", "''") + "'"


def main() -> None:
    lineas = [
        "-- Generado por backend/exportar_semilla.py desde PostgreSQL",
        f"-- Fecha: {datetime.now():%d/%m/%Y %H:%M}",
        "-- Los administradores no se exportan: créalos en Unity (Campus > Base de datos > Administradores).",
        "",
    ]

    with get_conn() as conn:
        for tabla, columnas in TABLAS:
            existe = conn.execute("SELECT to_regclass(%s) AS t", (tabla,)).fetchone()["t"]
            if not existe:
                print(f"  {tabla}: no existe en PostgreSQL, se omite")
                continue

            filas = conn.execute(f"SELECT {', '.join(columnas)} FROM {tabla} ORDER BY id").fetchall()
            print(f"  {tabla}: {len(filas)} filas")
            if not filas:
                continue

            lineas.append(f"-- {tabla}")
            valores = [
                "(" + ", ".join(literal(f[c]) for c in columnas) + ")" for f in filas
            ]
            # Lotes de 200 filas por INSERT para no hacer instrucciones gigantes
            for i in range(0, len(valores), 200):
                lineas.append(f"INSERT INTO {tabla} ({', '.join(columnas)}) VALUES")
                lineas.append(",\n".join(valores[i:i + 200]) + ";")
            lineas.append("")

    DESTINO.parent.mkdir(parents=True, exist_ok=True)
    DESTINO.write_text("\n".join(lineas), encoding="utf-8", newline="\n")
    print(f"\nListo: {DESTINO}")
    print("En Unity: Campus > Base de datos > Reconstruir desde semilla.sql")


if __name__ == "__main__":
    main()
