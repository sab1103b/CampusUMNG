import getpass

import bcrypt

from database import get_conn

nickname = input("Nickname del admin: ").strip()
llave = getpass.getpass("Llave (no se ve al escribir): ")

llave_hash = bcrypt.hashpw(llave.encode("utf-8"), bcrypt.gensalt()).decode("utf-8")

with get_conn() as conn:
    conn.execute(
        """
        INSERT INTO administradores (nickname, llave_hash)
        VALUES (%s, %s)
        ON CONFLICT (nickname) DO UPDATE SET llave_hash = EXCLUDED.llave_hash
        """,
        (nickname, llave_hash),
    )

print(f"Admin '{nickname}' guardado correctamente.")
