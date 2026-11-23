using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using SQLite4Unity3d;
using UnityEngine;
using UnityEngine.Networking;

/// <summary>Instalación de la base del APK en el celular y paso de los datos que solo existen ahí.</summary>
public static partial class BaseDatosLocal
{
    // ------------------------------------------------------------------
    // Aviso para quien usa el celular (se guarda en PlayerPrefs: sobrevive a cerrar la app)
    // ------------------------------------------------------------------
    private const string ClaveAvisoPendiente = "campus_aviso_base_datos";
    private const string MsgCuentaPerdida =
        "Tu cuenta de este celular no pudo conservarse en la actualización. Hay un respaldo; contacta al responsable.";

    /// <summary>
    /// Aviso pendiente para mostrar una vez (p. ej. en el login de administrador), o null si no hay.
    /// Se consume al leerlo: la siguiente lectura devuelve null. Hoy solo lo deja la actualización de la
    /// base cuando una cuenta de administrador del celular no se pudo pasar a la base nueva.
    /// Usar solo desde el hilo principal (PlayerPrefs).
    /// </summary>
    public static string AvisoPendiente
    {
        get
        {
            if (!PlayerPrefs.HasKey(ClaveAvisoPendiente)) return null;
            string aviso = PlayerPrefs.GetString(ClaveAvisoPendiente, "");
            PlayerPrefs.DeleteKey(ClaveAvisoPendiente);
            PlayerPrefs.Save();
            return string.IsNullOrEmpty(aviso) ? null : aviso;
        }
    }

    private static bool cuentaPerdida;   // ConservarDatosDelCelular no pudo pasar alguna cuenta del celular

    private static void GuardarAviso(string aviso)
    {
        PlayerPrefs.SetString(ClaveAvisoPendiente, aviso);
        PlayerPrefs.Save();
    }

    // ------------------------------------------------------------------
    // Copia desde el APK (Android)
    // ------------------------------------------------------------------
    private static IEnumerator CopiarDesdeApkSiHaceFalta()
    {
        string destino = RutaActiva;
        RestaurarPrevSiHaceFalta(destino);   // una copia no atómica interrumpida la vez anterior
        string temporal = Path.Combine(Application.temporaryCachePath, "campus_apk.db");

        // En Android StreamingAssets está dentro del APK: solo se puede leer con UnityWebRequest
        string origen = RutaMaestra;
        if (!origen.Contains("://")) origen = "file://" + origen;

        using (UnityWebRequest req = UnityWebRequest.Get(origen))
        {
            yield return req.SendWebRequest();

            if (req.result != UnityWebRequest.Result.Success)
            {
                Debug.LogWarning($"No hay base de datos dentro del APK ({req.error}). Se usará la local o una vacía.");
                yield break;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(temporal));
            File.WriteAllBytes(temporal, req.downloadHandler.data);
        }

        if (!File.Exists(destino))
        {
            if (InstalarBase(temporal, destino)) Registro.Info("Base de datos copiada desde el APK.");
        }
        else if (LeerVersion(temporal) > LeerVersion(destino))
        {
            cuentaPerdida = false;
            if (!ConservarDatosDelCelular(temporal, destino))
            {
                // No se pudo pasar todo a la base nueva: se guarda la del celular antes de reemplazarla.
                // El nombre lleva la versión de datos (campus.db.v{version}.bak): una actualización
                // posterior no sobrescribe el respaldo de otra versión.
                try
                {
                    string respaldo = destino + ".v" + LeerVersion(destino).ToString(CultureInfo.InvariantCulture) + ".bak";
                    File.Copy(destino, respaldo, true);
                    Debug.LogWarning("Algunos datos del celular no se pasaron a la base nueva. Copia guardada en: " + respaldo);
                }
                catch (Exception e) { Debug.LogWarning("No se pudo respaldar la base del celular: " + e.Message); }
            }
            if (InstalarBase(temporal, destino))
            {
                Registro.Info("Base de datos actualizada con la versión del APK (más nueva).");
                // Solo si la base nueva quedó instalada (si no, la cuenta sigue en la base vieja del celular)
                if (cuentaPerdida) GuardarAviso(MsgCuentaPerdida);
            }
        }

        try { File.Delete(temporal); } catch (Exception) { }
    }

    /// <summary>
    /// Pone 'origen' en 'destino' sin dejar nunca una base a medio copiar: copia a destino + ".nueva",
    /// comprueba la copia con PRAGMA integrity_check y la cambia por la vieja con un reemplazo atómico
    /// (rename). Si algo falla, la base que ya había queda intacta. Devuelve true si se instaló.
    /// </summary>
    private static bool InstalarBase(string origen, string destino)
    {
        string nueva = destino + ".nueva";
        try
        {
            File.Copy(origen, nueva, true);
            if (!BaseIntegra(nueva))
            {
                Debug.LogWarning("La base de datos del APK no pasó la verificación de integridad; se conserva la actual.");
                File.Delete(nueva);
                return false;
            }

            if (!File.Exists(destino)) File.Move(nueva, destino);
            else
            {
                try { File.Replace(nueva, destino, null); }
                catch (Exception)
                {
                    // Sin reemplazo atómico en esta plataforma: la copia ya está verificada. Antes de sobrescribir,
                    // la base actual se guarda en destino + ".prev": si la copia se corta (batería, cierre), al
                    // abrir se restaura (RestaurarPrevSiHaceFalta). Con la copia completa e íntegra, .prev se borra.
                    string prev = destino + SufijoPrev;
                    File.Copy(destino, prev, true);
                    File.Copy(nueva, destino, true);
                    File.Delete(nueva);
                    if (BaseIntegra(destino)) File.Delete(prev);
                    else
                    {
                        File.Copy(prev, destino, true);
                        File.Delete(prev);
                        Debug.LogWarning("La copia de la base nueva quedó dañada; se restauró la anterior.");
                        return false;
                    }
                }
            }

            // Solo ahora (la base nueva ya está en su sitio): un diario (journal) que quedó de la base vieja no
            // debe aplicarse sobre la nueva. Antes del reemplazo no se toca: si el reemplazo fallara, la base
            // vieja todavía lo necesitaría para recuperarse.
            string diario = destino + "-journal";
            try { if (File.Exists(diario)) File.Delete(diario); }
            catch (Exception e) { Debug.LogWarning("No se pudo borrar el diario de la base anterior: " + e.Message); }
            return true;
        }
        catch (Exception e)
        {
            Debug.LogWarning("No se pudo instalar la base de datos del APK: " + e.Message);
            try { if (File.Exists(nueva)) File.Delete(nueva); } catch (Exception) { }
            return false;
        }
    }

    private const string SufijoPrev = ".prev";

    /// <summary>
    /// Al abrir (Android): si quedó un destino + ".prev" de una copia no atómica interrumpida y la base
    /// no está íntegra, se restaura la anterior. Si la base está bien, el .prev sobrante se borra.
    /// </summary>
    private static void RestaurarPrevSiHaceFalta(string destino)
    {
        string prev = destino + SufijoPrev;
        try
        {
            if (!File.Exists(prev)) return;
            if (File.Exists(destino) && BaseIntegra(destino)) { File.Delete(prev); return; }
            if (!BaseIntegra(prev)) { Debug.LogWarning("El respaldo .prev de la base tampoco está íntegro; no se usa."); return; }
            File.Copy(prev, destino, true);
            File.Delete(prev);
            Debug.LogWarning("La base de datos estaba dañada (copia interrumpida); se restauró la versión anterior.");
        }
        catch (Exception e) { Debug.LogWarning("No se pudo revisar el respaldo .prev de la base: " + e.Message); }
    }

    private static bool BaseIntegra(string ruta)
    {
        try
        {
            using (var c = new SQLiteConnection(ruta, SQLiteOpenFlags.ReadOnly))
                return string.Equals(c.ExecuteScalar<string>("PRAGMA integrity_check"), "ok", StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception e)
        {
            Debug.LogWarning("No se pudo verificar la base de datos: " + e.Message);
            return false;
        }
    }

    /// <summary>
    /// Antes de reemplazar la base del celular por la del APK nuevo, pasa a la nueva lo que
    /// solo existe en el celular: las cuentas de administrador registradas ahí (por su código)
    /// y las búsquedas que el APK no trae ya (mismo lugar y misma fecha_hora).
    ///
    /// Política al actualizar el APK (lo que NO se conserva):
    ///  - Las ediciones hechas en el celular con el modo administrador (lugares, servicios, nodos,
    ///    caminos y modelos 3D) se reemplazan por los datos del APK nuevo. Es intencional: la base
    ///    maestra del proyecto es la fuente de verdad. Quien administra debe saberlo: los cambios
    ///    permanentes se hacen en el Editor (o se repiten ahí) antes de compilar la versión nueva.
    ///  - Las búsquedas del celular de lugares que ya no existen en el APK (y las que el APK ya trae).
    ///    Límite conocido: no se marca cuáles búsquedas del celular vinieron del APK anterior, así que si
    ///    se borraron de la maestra (p. ej. pruebas que ya habían viajado en un APK), vuelven a pasar.
    ///    El prebuild (BaseDatosPreBuild) pide limpiarlas antes de compilar para evitarlo.
    ///  - Una cuenta registrada en el celular cuyo espacio ya no existe en el APK, o que en la base
    ///    maestra ya registró otra persona, o cuyo usuario ya existe en la maestra.
    /// Devuelve false si algo no se pudo pasar (quien llama respalda la base del celular).
    /// </summary>
    private static bool ConservarDatosDelCelular(string baseNueva, string baseCelular)
    {
        bool todoBien = true;
        try
        {
            using (var c = new SQLiteConnection(baseNueva, SQLiteOpenFlags.ReadWrite))
            {
                CrearTablas(c);   // por si el APK trae una base de formato anterior
                c.Execute("ATTACH DATABASE ? AS viejo", baseCelular);
                try
                {
                    // Bloque de cuentas en UNA transacción: o pasan todas (por código y migradas) o ninguna,
                    // y entonces quien llama respalda la base del celular
                    c.RunInTransaction(() =>
                    {
                        c.Execute(
                            @"UPDATE administradores SET
                                  nickname      = (SELECT v.nickname      FROM viejo.administradores v WHERE v.codigo_hash = administradores.codigo_hash),
                                  llave_hash    = (SELECT v.llave_hash    FROM viejo.administradores v WHERE v.codigo_hash = administradores.codigo_hash),
                                  registrado_en = (SELECT v.registrado_en FROM viejo.administradores v WHERE v.codigo_hash = administradores.codigo_hash)
                              WHERE nickname IS NULL
                                AND codigo_hash IN (SELECT codigo_hash FROM viejo.administradores WHERE nickname IS NOT NULL)
                                AND lower((SELECT v.nickname FROM viejo.administradores v WHERE v.codigo_hash = administradores.codigo_hash))
                                    NOT IN (SELECT lower(nickname) FROM administradores WHERE nickname IS NOT NULL)");

                        // Códigos migrados de SHA-256 a PBKDF2: el hash cambió, así que se empareja por id, solo
                        // para los espacios anotados en meta y (si hay huella) solo si el código viejo es el mismo.
                        PasarCuentasMigradas(c);

                        // Cuentas del celular que no quedaron en la base nueva (usuario ya tomado en la maestra,
                        // espacio eliminado...): se avisa para que quien llama respalde la base del celular.
                        int perdidas = c.ExecuteScalar<int>(
                            @"SELECT COUNT(*) FROM viejo.administradores v
                              WHERE v.nickname IS NOT NULL
                                AND NOT EXISTS (SELECT 1 FROM administradores a
                                                WHERE a.nickname = v.nickname AND a.llave_hash = v.llave_hash)");
                        if (perdidas > 0)
                        {
                            todoBien = false;
                            cuentaPerdida = true;   // se avisa una vez en el login si la base nueva se instala (AvisoPendiente)
                            Debug.LogWarning($"{perdidas} cuenta(s) de administrador del celular no se pudieron pasar a la base nueva.");
                        }
                    });
                }
                catch (Exception e)
                {
                    todoBien = false;
                    if (HayCuentasRegistradas(baseCelular)) cuentaPerdida = true;
                    Debug.LogWarning("No se pudieron conservar las cuentas del celular: " + e.Message);
                }

                try
                {
                    // Se pasan las búsquedas del celular que la maestra no trae ya (mismo lugar y misma
                    // fecha_hora; usa el índice idx_busquedas_lugar_fecha). Así no se pierden las búsquedas
                    // viejas del celular aunque la maestra traiga alguna más reciente, ni se duplican las que
                    // ya vienen en el APK. SQLite evalúa el SELECT antes de insertar: dos búsquedas del
                    // celular en el mismo segundo pasan las dos.
                    // El lugar debe ser EL MISMO, no solo el mismo id: un lugar creado en el celular y otro creado
                    // en la maestra pueden compartir id. Se exige el mismo nombre (sin mayúsculas ni espacios
                    // sobrantes). Límite: si en la maestra se renombró el lugar, sus búsquedas del celular no pasan.
                    c.Execute(
                        @"INSERT INTO busquedas_log (lugar_id, fecha_hora)
                          SELECT v.lugar_id, v.fecha_hora FROM viejo.busquedas_log v
                          WHERE EXISTS (SELECT 1 FROM lugares l JOIN viejo.lugares vl ON vl.id = l.id
                                        WHERE l.id = v.lugar_id AND lower(trim(l.nombre)) = lower(trim(vl.nombre)))
                            AND NOT EXISTS (SELECT 1 FROM busquedas_log b
                                            WHERE b.lugar_id = v.lugar_id AND b.fecha_hora = v.fecha_hora)");
                }
                catch (Exception e) { todoBien = false; Debug.LogWarning("No se pudieron conservar las búsquedas del celular: " + e.Message); }

                c.Execute("DETACH DATABASE viejo");
            }
        }
        catch (Exception e)
        {
            if (HayCuentasRegistradas(baseCelular)) cuentaPerdida = true;
            Debug.LogWarning("No se pudieron conservar los datos del celular: " + e.Message);
            return false;
        }
        return todoBien;
    }

    // ¿La base del celular tiene alguna cuenta de administrador registrada? (para no avisar en vano)
    private static bool HayCuentasRegistradas(string ruta)
    {
        try
        {
            using (var c = new SQLiteConnection(ruta, SQLiteOpenFlags.ReadOnly))
                return c.ExecuteScalar<int>("SELECT COUNT(*) FROM administradores WHERE nickname IS NOT NULL") > 0;
        }
        catch (Exception) { return true; }   // no se pudo leer: mejor avisar
    }

    // Cuenta registrada en la base vieja del celular (solo para PasarCuentasMigradas)
    private class CuentaVieja
    {
        public int id { get; set; }
        public string nickname { get; set; }
        public string llave_hash { get; set; }
        public string registrado_en { get; set; }
        public string codigo_hash { get; set; }
    }

    /// <summary>
    /// Pasa las cuentas registradas en el celular a los espacios cuyo código se migró de SHA-256 a PBKDF2
    /// (la maestra ya no tiene el mismo hash, así que no se pueden emparejar por hash).
    /// Reglas (meta.codigos_migrados_pbkdf2 = "id" o "id:huella", separados por coma):
    ///  - "id:huella": la cuenta del celular pasa solo si su hash viejo corresponde a la huella, es decir,
    ///    si se registró con el MISMO código. Un espacio distinto que reutilizara el id no la recibe.
    ///  - "id" solo (migración anterior sin huella, p. ej. los espacios 2–5): se acepta mientras el id siga
    ///    en la lista. Es seguro porque los id ya no se reutilizan (meta.ultimo_id_admin), al eliminar un
    ///    espacio su id sale de la lista, y en el Editor se completa la huella cuando el código está en
    ///    CodigosRegistroAdmin.txt (RepositorioCampus.CompletarHuellasMigradas).
    /// En ambos casos: el espacio de la base nueva debe seguir libre y el usuario no puede existir ya.
    /// </summary>
    private static void PasarCuentasMigradas(SQLiteConnection c)
    {
        Dictionary<int, string> migrados = LeerCodigosMigrados(c);
        foreach (KeyValuePair<int, string> par in migrados)
        {
            List<CuentaVieja> viejas = c.Query<CuentaVieja>(
                @"SELECT id, nickname, llave_hash, registrado_en, codigo_hash FROM viejo.administradores
                  WHERE id = ? AND nickname IS NOT NULL", par.Key);
            if (viejas.Count == 0) continue;
            CuentaVieja v = viejas[0];
            if (!SeguridadLlaves.EsHashCodigoViejo(v.codigo_hash)) continue;
            if (par.Value != null && !SeguridadLlaves.CoincideHuellaCodigoViejo(v.codigo_hash, par.Value)) continue;
            if (c.ExecuteScalar<int>("SELECT COUNT(*) FROM administradores WHERE lower(nickname) = lower(?)", v.nickname) > 0) continue;

            c.Execute(
                @"UPDATE administradores SET nickname = ?, llave_hash = ?, registrado_en = ?
                  WHERE id = ? AND nickname IS NULL AND codigo_hash LIKE 'pbkdf2-sha256$%'",
                v.nickname, v.llave_hash, v.registrado_en, par.Key);
        }
    }

    /// <summary>
    /// Lee meta.codigos_migrados_pbkdf2: id del espacio → huella del hash viejo (null si solo se anotó el id).
    /// Formato: "2,3:pbkdf2-sha256$...". La huella no tiene ',' ni ':' (base64 y '$').
    /// </summary>
    internal static Dictionary<int, string> LeerCodigosMigrados(SQLiteConnection c)
    {
        Dictionary<int, string> mapa = new Dictionary<int, string>();
        string valor = c.ExecuteScalar<string>("SELECT valor FROM meta WHERE clave = 'codigos_migrados_pbkdf2'");
        if (string.IsNullOrEmpty(valor)) return mapa;
        foreach (string parte in valor.Split(','))
        {
            string p = parte.Trim();
            int dosPuntos = p.IndexOf(':');
            string id = dosPuntos >= 0 ? p.Substring(0, dosPuntos) : p;
            string huella = dosPuntos >= 0 ? p.Substring(dosPuntos + 1) : null;
            if (int.TryParse(id, NumberStyles.Integer, CultureInfo.InvariantCulture, out int n) && n > 0)
                mapa[n] = string.IsNullOrEmpty(huella) ? null : huella;
        }
        return mapa;
    }

    /// <summary>Guarda la lista (o borra la clave si quedó vacía). Quien llama abre la transacción.</summary>
    internal static void GuardarCodigosMigrados(SQLiteConnection c, Dictionary<int, string> mapa)
    {
        if (mapa.Count == 0)
        {
            c.Execute("DELETE FROM meta WHERE clave = 'codigos_migrados_pbkdf2'");
            return;
        }
        List<int> ids = new List<int>(mapa.Keys);
        ids.Sort();
        List<string> partes = ids.ConvertAll(id => mapa[id] == null
            ? id.ToString(CultureInfo.InvariantCulture)
            : id.ToString(CultureInfo.InvariantCulture) + ":" + mapa[id]);
        c.Execute("INSERT OR REPLACE INTO meta (clave, valor) VALUES ('codigos_migrados_pbkdf2', ?)", string.Join(",", partes));
    }

    private static int LeerVersion(string ruta)
    {
        try
        {
            using (var c = new SQLiteConnection(ruta, SQLiteOpenFlags.ReadOnly))
                return c.ExecuteScalar<int>("PRAGMA user_version");
        }
        catch (Exception e)
        {
            Debug.LogWarning($"No se pudo leer la versión de {ruta}: {e.Message}");
            return 0;
        }
    }
}
