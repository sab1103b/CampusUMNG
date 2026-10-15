import secrets
import time
from datetime import datetime
from io import BytesIO
from typing import Optional

import bcrypt
import psycopg
from fastapi import Depends, FastAPI, HTTPException
from fastapi.middleware.cors import CORSMiddleware
from fastapi.responses import Response
from fastapi.security import HTTPAuthorizationCredentials, HTTPBearer
from pydantic import BaseModel, Field
from reportlab.lib import colors
from reportlab.lib.pagesizes import A4
from reportlab.lib.styles import getSampleStyleSheet
from reportlab.platypus import Paragraph, SimpleDocTemplate, Spacer, Table, TableStyle

from database import get_conn

app = FastAPI(title="API Campus Cajicá")

# Permite que la app (Unity) y el navegador consulten la API
app.add_middleware(
    CORSMiddleware,
    allow_origins=["*"],
    allow_methods=["*"],
    allow_headers=["*"],
)

# ---------- Sesiones de administrador ----------
# Los tokens viven en memoria: si reinicias el servidor, el admin vuelve a iniciar sesión.
TOKENS: dict[str, dict] = {}
DURACION_SESION = 2 * 60 * 60  # 2 horas
seguridad = HTTPBearer()


def admin_actual(cred: HTTPAuthorizationCredentials = Depends(seguridad)) -> str:
    datos = TOKENS.get(cred.credentials)
    if not datos or datos["expira"] < time.time():
        TOKENS.pop(cred.credentials, None)
        raise HTTPException(status_code=401, detail="Sesión inválida o expirada")
    return datos["nickname"]


# ---------- Modelos ----------
class BusquedaIn(BaseModel):
    lugar_id: int


class LoginIn(BaseModel):
    nickname: str
    llave: str


# ---------- Endpoints públicos ----------
@app.get("/")
def inicio():
    return {"mensaje": "API Campus Cajicá funcionando"}


@app.get("/api/lugares")
def listar_lugares():
    with get_conn() as conn:
        filas = conn.execute(
            """
            SELECT id, nombre, categoria,
                   COALESCE(descripcion, '') AS descripcion,
                   COALESCE(indicacion, '') AS indicacion,
                   COALESCE(horario, '') AS horario,
                   latitud, longitud
            FROM lugares
            ORDER BY nombre
            """
        ).fetchall()
    return filas


@app.post("/api/busquedas/registrar", status_code=201)
def registrar_busqueda(data: BusquedaIn):
    with get_conn() as conn:
        existe = conn.execute(
            "SELECT 1 FROM lugares WHERE id = %s", (data.lugar_id,)
        ).fetchone()
        if not existe:
            raise HTTPException(status_code=404, detail="Lugar no encontrado")
        conn.execute(
            "INSERT INTO busquedas_log (lugar_id) VALUES (%s)", (data.lugar_id,)
        )
    return {"ok": True}


# ---------- Endpoints de administrador ----------
@app.post("/api/admin/login")
def login_admin(data: LoginIn):
    with get_conn() as conn:
        fila = conn.execute(
            "SELECT llave_hash FROM administradores WHERE nickname = %s",
            (data.nickname,),
        ).fetchone()

    llave = data.llave.encode("utf-8")[:72]  # bcrypt solo usa los primeros 72 bytes
    if not fila or not bcrypt.checkpw(llave, fila["llave_hash"].encode("utf-8")):
        # Mismo mensaje para usuario o llave incorrectos (no revela cuál falló)
        raise HTTPException(status_code=401, detail="Credenciales incorrectas")

    token = secrets.token_urlsafe(32)
    TOKENS[token] = {
        "nickname": data.nickname,
        "expira": time.time() + DURACION_SESION,
    }
    return {"token": token, "expira_en_segundos": DURACION_SESION}


@app.get("/api/admin/reporte-pdf")
def reporte_pdf(admin: str = Depends(admin_actual)):
    with get_conn() as conn:
        filas = conn.execute(
            """
            SELECT l.nombre, l.categoria, COUNT(b.id) AS total
            FROM lugares l
            LEFT JOIN busquedas_log b ON b.lugar_id = l.id
            GROUP BY l.id, l.nombre, l.categoria
            ORDER BY total DESC, l.nombre
            LIMIT 10
            """
        ).fetchall()
        total = conn.execute("SELECT COUNT(*) AS n FROM busquedas_log").fetchone()["n"]

    # Construir el PDF en memoria
    buffer = BytesIO()
    doc = SimpleDocTemplate(buffer, pagesize=A4, title="Reporte de búsquedas")
    estilos = getSampleStyleSheet()

    contenido = [
        Paragraph("Reporte de lugares más buscados", estilos["Title"]),
        Paragraph("Campus Cajicá", estilos["Heading3"]),
        Paragraph(
            f"Generado: {datetime.now():%d/%m/%Y %H:%M} &nbsp;|&nbsp; "
            f"Total de búsquedas: {total}",
            estilos["Normal"],
        ),
        Spacer(1, 20),
    ]

    datos = [["#", "Lugar", "Categoría", "Búsquedas"]]
    for i, f in enumerate(filas, start=1):
        datos.append([str(i), f["nombre"], f["categoria"], str(f["total"])])

    tabla = Table(datos, colWidths=[30, 220, 130, 70])
    tabla.setStyle(
        TableStyle(
            [
                ("BACKGROUND", (0, 0), (-1, 0), colors.HexColor("#1F3A5F")),
                ("TEXTCOLOR", (0, 0), (-1, 0), colors.white),
                ("FONTNAME", (0, 0), (-1, 0), "Helvetica-Bold"),
                ("ALIGN", (0, 0), (0, -1), "CENTER"),
                ("ALIGN", (3, 0), (3, -1), "CENTER"),
                ("ROWBACKGROUNDS", (0, 1), (-1, -1), [colors.white, colors.HexColor("#F0F3F7")]),
                ("GRID", (0, 0), (-1, -1), 0.5, colors.HexColor("#C5CCD6")),
                ("TOPPADDING", (0, 0), (-1, -1), 6),
                ("BOTTOMPADDING", (0, 0), (-1, -1), 6),
            ]
        )
    )
    contenido.append(tabla)
    doc.build(contenido)

    return Response(
        content=buffer.getvalue(),
        media_type="application/pdf",
        headers={"Content-Disposition": 'attachment; filename="reporte_busquedas.pdf"'},
    )


# ======================================================
# Red de caminos (pública): la descarga la app al abrir
# ======================================================
@app.get("/api/red")
def obtener_red():
    with get_conn() as conn:
        nodos = conn.execute(
            """
            SELECT id, COALESCE(nombre, '') AS nombre, latitud, longitud,
                   COALESCE(lugar_id, 0) AS lugar_id
            FROM nodos
            ORDER BY id
            """
        ).fetchall()
        conexiones = conn.execute(
            "SELECT id, nodo_a, nodo_b FROM conexiones ORDER BY id"
        ).fetchall()
    return {"nodos": nodos, "conexiones": conexiones}


# ======================================================
# Administración de lugares, nodos y conexiones (requiere token)
# ======================================================
class LugarIn(BaseModel):
    nombre: str = Field(min_length=1, max_length=120)
    categoria: str = Field(min_length=1, max_length=50)
    descripcion: Optional[str] = None
    indicacion: Optional[str] = None
    horario: Optional[str] = Field(default=None, max_length=120)
    latitud: float = Field(ge=-90, le=90)
    longitud: float = Field(ge=-180, le=180)


class LugarCrearIn(LugarIn):
    # Si se envía, se crea también el nodo-puerta del lugar y se une a este nodo existente
    conectar_con_nodo: Optional[int] = None


class NodoIn(BaseModel):
    nombre: Optional[str] = Field(default=None, max_length=80)
    latitud: float = Field(ge=-90, le=90)
    longitud: float = Field(ge=-180, le=180)
    lugar_id: Optional[int] = None
    conectar_con_nodo: Optional[int] = None   # solo se usa al crear


class ConexionIn(BaseModel):
    nodo_a: int
    nodo_b: int


def _par(a: int, b: int) -> tuple[int, int]:
    if a == b:
        raise HTTPException(status_code=400, detail="Un nodo no puede conectarse consigo mismo")
    return (a, b) if a < b else (b, a)


@app.post("/api/admin/lugares", status_code=201)
def crear_lugar(data: LugarCrearIn, admin: str = Depends(admin_actual)):
    try:
        with get_conn() as conn:
            lugar = conn.execute(
                """
                INSERT INTO lugares (nombre, categoria, descripcion, indicacion, horario, latitud, longitud)
                VALUES (%s, %s, %s, %s, %s, %s, %s) RETURNING id
                """,
                (data.nombre, data.categoria, data.descripcion, data.indicacion,
                 data.horario, data.latitud, data.longitud),
            ).fetchone()

            nodo_id = None
            if data.conectar_con_nodo:
                existe = conn.execute(
                    "SELECT 1 FROM nodos WHERE id = %s", (data.conectar_con_nodo,)
                ).fetchone()
                if not existe:
                    raise HTTPException(status_code=404, detail="El nodo a conectar no existe")

                nodo = conn.execute(
                    """
                    INSERT INTO nodos (nombre, latitud, longitud, lugar_id)
                    VALUES (%s, %s, %s, %s) RETURNING id
                    """,
                    (data.nombre, data.latitud, data.longitud, lugar["id"]),
                ).fetchone()
                nodo_id = nodo["id"]

                a, b = _par(nodo_id, data.conectar_con_nodo)
                conn.execute("INSERT INTO conexiones (nodo_a, nodo_b) VALUES (%s, %s)", (a, b))
    except psycopg.errors.UniqueViolation:
        raise HTTPException(status_code=409, detail="Registro duplicado")
    return {"id": lugar["id"], "nodo_id": nodo_id}


@app.put("/api/admin/lugares/{lugar_id}")
def actualizar_lugar(lugar_id: int, data: LugarIn, admin: str = Depends(admin_actual)):
    with get_conn() as conn:
        cur = conn.execute(
            """
            UPDATE lugares
            SET nombre = %s, categoria = %s, descripcion = %s, indicacion = %s,
                horario = %s, latitud = %s, longitud = %s
            WHERE id = %s
            """,
            (data.nombre, data.categoria, data.descripcion, data.indicacion,
             data.horario, data.latitud, data.longitud, lugar_id),
        )
        if cur.rowcount == 0:
            raise HTTPException(status_code=404, detail="Lugar no encontrado")
    return {"ok": True}


@app.delete("/api/admin/lugares/{lugar_id}")
def eliminar_lugar(lugar_id: int, admin: str = Depends(admin_actual)):
    # Su nodo-puerta (si existe) se queda como nodo de paso; el historial de búsquedas del lugar se borra.
    with get_conn() as conn:
        cur = conn.execute("DELETE FROM lugares WHERE id = %s", (lugar_id,))
        if cur.rowcount == 0:
            raise HTTPException(status_code=404, detail="Lugar no encontrado")
    return {"ok": True}


@app.post("/api/admin/nodos", status_code=201)
def crear_nodo(data: NodoIn, admin: str = Depends(admin_actual)):
    try:
        with get_conn() as conn:
            nodo = conn.execute(
                """
                INSERT INTO nodos (nombre, latitud, longitud, lugar_id)
                VALUES (%s, %s, %s, %s) RETURNING id
                """,
                (data.nombre, data.latitud, data.longitud, data.lugar_id or None),
            ).fetchone()

            if data.conectar_con_nodo:
                a, b = _par(nodo["id"], data.conectar_con_nodo)
                conn.execute("INSERT INTO conexiones (nodo_a, nodo_b) VALUES (%s, %s)", (a, b))
    except psycopg.errors.ForeignKeyViolation:
        raise HTTPException(status_code=404, detail="El lugar o el nodo indicado no existe")
    return {"id": nodo["id"]}


@app.put("/api/admin/nodos/{nodo_id}")
def actualizar_nodo(nodo_id: int, data: NodoIn, admin: str = Depends(admin_actual)):
    try:
        with get_conn() as conn:
            cur = conn.execute(
                "UPDATE nodos SET nombre = %s, latitud = %s, longitud = %s, lugar_id = %s WHERE id = %s",
                (data.nombre, data.latitud, data.longitud, data.lugar_id or None, nodo_id),
            )
            if cur.rowcount == 0:
                raise HTTPException(status_code=404, detail="Nodo no encontrado")
    except psycopg.errors.ForeignKeyViolation:
        raise HTTPException(status_code=404, detail="El lugar indicado no existe")
    return {"ok": True}


@app.delete("/api/admin/nodos/{nodo_id}")
def eliminar_nodo(nodo_id: int, admin: str = Depends(admin_actual)):
    # Sus conexiones se borran solas. Ojo: puede dejar partes de la red desconectadas.
    with get_conn() as conn:
        cur = conn.execute("DELETE FROM nodos WHERE id = %s", (nodo_id,))
        if cur.rowcount == 0:
            raise HTTPException(status_code=404, detail="Nodo no encontrado")
    return {"ok": True}


@app.post("/api/admin/conexiones", status_code=201)
def crear_conexion(data: ConexionIn, admin: str = Depends(admin_actual)):
    a, b = _par(data.nodo_a, data.nodo_b)
    try:
        with get_conn() as conn:
            conn.execute("INSERT INTO conexiones (nodo_a, nodo_b) VALUES (%s, %s)", (a, b))
    except psycopg.errors.ForeignKeyViolation:
        raise HTTPException(status_code=404, detail="Alguno de los nodos no existe")
    except psycopg.errors.UniqueViolation:
        raise HTTPException(status_code=409, detail="Esos nodos ya están conectados")
    return {"ok": True}


@app.delete("/api/admin/conexiones/{nodo_a}/{nodo_b}")
def eliminar_conexion(nodo_a: int, nodo_b: int, admin: str = Depends(admin_actual)):
    a, b = _par(nodo_a, nodo_b)
    with get_conn() as conn:
        cur = conn.execute(
            "DELETE FROM conexiones WHERE nodo_a = %s AND nodo_b = %s", (a, b)
        )
        if cur.rowcount == 0:
            raise HTTPException(status_code=404, detail="Esa conexión no existe")
    return {"ok": True}


# ======================================================
# Servicios de cada lugar (salones, oficinas, laboratorios...)
# ======================================================
@app.get("/api/servicios")
def listar_servicios():
    with get_conn() as conn:
        filas = conn.execute(
            """
            SELECT id, lugar_id, nombre,
                   COALESCE(categoria, '') AS categoria,
                   COALESCE(piso, '') AS piso,
                   COALESCE(descripcion, '') AS descripcion,
                   COALESCE(indicacion, '') AS indicacion,
                   COALESCE(horario, '') AS horario
            FROM servicios
            ORDER BY lugar_id, nombre
            """
        ).fetchall()
    return filas


class ServicioIn(BaseModel):
    lugar_id: int = Field(ge=1)
    nombre: str = Field(min_length=1, max_length=120)
    categoria: Optional[str] = Field(default=None, max_length=50)
    piso: Optional[str] = Field(default=None, max_length=20)
    descripcion: Optional[str] = None
    indicacion: Optional[str] = None
    horario: Optional[str] = Field(default=None, max_length=120)


@app.post("/api/admin/servicios", status_code=201)
def crear_servicio(data: ServicioIn, admin: str = Depends(admin_actual)):
    try:
        with get_conn() as conn:
            fila = conn.execute(
                """
                INSERT INTO servicios (lugar_id, nombre, categoria, piso, descripcion, indicacion, horario)
                VALUES (%s, %s, %s, %s, %s, %s, %s) RETURNING id
                """,
                (data.lugar_id, data.nombre, data.categoria, data.piso,
                 data.descripcion, data.indicacion, data.horario),
            ).fetchone()
    except psycopg.errors.ForeignKeyViolation:
        raise HTTPException(status_code=404, detail="El lugar indicado no existe")
    return {"id": fila["id"]}


@app.put("/api/admin/servicios/{servicio_id}")
def actualizar_servicio(servicio_id: int, data: ServicioIn, admin: str = Depends(admin_actual)):
    try:
        with get_conn() as conn:
            cur = conn.execute(
                """
                UPDATE servicios
                SET lugar_id = %s, nombre = %s, categoria = %s, piso = %s,
                    descripcion = %s, indicacion = %s, horario = %s
                WHERE id = %s
                """,
                (data.lugar_id, data.nombre, data.categoria, data.piso,
                 data.descripcion, data.indicacion, data.horario, servicio_id),
            )
            if cur.rowcount == 0:
                raise HTTPException(status_code=404, detail="Servicio no encontrado")
    except psycopg.errors.ForeignKeyViolation:
        raise HTTPException(status_code=404, detail="El lugar indicado no existe")
    return {"ok": True}


@app.delete("/api/admin/servicios/{servicio_id}")
def eliminar_servicio(servicio_id: int, admin: str = Depends(admin_actual)):
    with get_conn() as conn:
        cur = conn.execute("DELETE FROM servicios WHERE id = %s", (servicio_id,))
        if cur.rowcount == 0:
            raise HTTPException(status_code=404, detail="Servicio no encontrado")
    return {"ok": True}