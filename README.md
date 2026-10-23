# Lodeon Admin

Gestor y diagnosticador de mods para **RimWorld 1.7** (motor Waylith, .NET 10).
Windows, WinForms, sin dependencias externas.

Nació para resolver un problema concreto que otros gestores no cubren: RimWorld solo
carga la carpeta de idioma cuyo nombre coincide **exactamente** con `langFolderName`
de `Prefs.xml`. Un mod que trae `Languages\Spanish` es ignorado si el juego espera
`Spanish (Español(Castellano))`. Lodeon Admin detecta esos casos y crea enlaces con el
nombre esperado.

---

## Qué hace

### Estado
Versión del juego, mods activos e instalados, análisis del registro (`Player.log`),
idioma activo. Refresco automático del contador de errores.

### Salud
Un solo informe con todo lo que puede ir mal:

- **Atribuye los errores del registro a un mod concreto.** RimWorld no escribe el nombre
  del mod en la línea de error, pero las trazas incluyen marcos `at Namespace.Clase(...)`
  y ese namespace coincide con el ensamblado, un segmento del `packageId` o el nombre de
  la carpeta. Se usan las tres vías.
- Dependencias rotas (ausentes o desactivadas).
- Incompatibilidades declaradas entre mods activos.
- Reglas de orden incumplidas.
- Mods que declaran versiones antiguas **y** además aparecen en el registro.
- Botón para desactivar el mod señalado sin salir de la página.

### Workshop
Búsqueda por nombre con la Web API de Steam, consulta directa por ID o enlace,
descarga por SteamCMD e instalación en la carpeta de mods, detección de actualizaciones
comparando fechas. La clave de Steam se gestiona desde la propia interfaz y se puede
probar.

### Mods
Mantenimiento con una línea de explicación por acción: limpiar copias de seguridad,
comprobar dependencias, abrir carpetas, recrear enlaces de idioma, ordenar la lista
por reglas, comprobar incompatibilidades.

### Lista
Inventario completo, casillas, filtro y paginación. Al escribir `ModsConfig.xml`
conserva el BOM, guarda copia, verifica el resultado y **devuelve cada mod a su índice
original** si lo reactivas. Los mods sin `About.xml` aparecen marcados en vez de
desaparecer.

### Idioma
Inventario de los 29 idiomas presentes en los mods, detección del idioma del sistema,
creación y retirada de enlaces de traducción, cuarentena reversible de carpetas.

---

## Ordenación por reglas

Lee `loadAfter` y `loadBefore` de cada `About.xml`, construye el grafo y lo ordena con
el algoritmo de Kahn. El nivel (núcleo, frameworks, resto) se usa solo como **desempate**,
nunca para separar el grafo: separarlo rompe las reglas que cruzan niveles.

Si hay un ciclo, lo rompe de forma determinista y lo informa. Nunca se pierde ni se
duplica un mod.

Comprobado con una instalación de 405 mods y 807 reglas: **807 de 807 respetadas**.

---

## Compilar

Requiere **.NET 8 SDK** o superior y Windows.

```powershell
dotnet build -c Release
dotnet publish -c Release -r win-x64 --self-contained false `
    -p:PublishSingleFile=false -o publish
```

El paquete resultante necesita **.NET 8 Desktop Runtime** instalado.

> No uses `PublishSingleFile=true`: en este proyecto genera un ejecutable que arranca
> sin interfaz y descarta los argumentos de consola.

---

## Comandos de consola

Sirven para diagnosticar sin abrir la ventana:

| Comando | Qué hace |
|---|---|
| `-selftest` | Comprueba rutas, archivos y configuración; escribe `selftest.txt` |
| `-health` | Informe de salud completo; escribe `health.txt` |
| `-sortreport` | Ordena la lista y verifica que se respeten todas las reglas |
| `-configcheck` | Estado de `ModsConfig.xml` y de sus copias |
| `-detect` | Qué juego y qué SteamCMD encuentra la detección automática |
| `-langreport` | Inventario de idiomas por mod |
| `-page <nombre>` | Abre la interfaz en una página concreta |

Todos devuelven código de salida distinto de cero si encuentran un problema.

---

### Ajustes
Página para indicar dónde está el juego si la detección falla:

- **Carpeta del juego** — valida que contenga `RimWorld.exe`.
- **SteamCMD** — solo necesario para descargar del Workshop.
- **Carpeta de copias** — dónde guardar las versiones anteriores.
- Botón **Detectar automáticamente** y **Examinar** para elegir a mano.

## Detección automática

La app no da por hecho que el juego esté en un sitio concreto. Busca, en orden:

1. Una instalación portable dentro de su propia carpeta.
2. El registro de Steam (`HKCU\Software\Valve\Steam`, `SteamPath`).
3. `steamapps\libraryfolders.vdf`, que declara las bibliotecas adicionales:
   mucha gente tiene el juego en otra unidad.
4. Ubicaciones habituales, como último recurso.

Si la ruta guardada deja de existir, **se vuelve a detectar y avisa** en vez de
mostrar datos vacíos. Con `-detect` puedes ver qué encuentra sin abrir la ventana.

## Configuración

Se lee de `config.json`, junto al ejecutable. Se crea con lo detectado la primera
vez, así que un usuario nuevo no tiene que editar nada a mano.

```json
{
  "SteamApiKey": "",
  "SteamCmd": "C:\\Ruta\\a\\steamcmd.exe",
  "GamePath": "C:\\Ruta\\a\\RimWorld",
  "VersionsFolder": "C:\\Ruta\\a\\copias",
  "DetectarIdioma": true,
  "AvisarActualizaciones": true
}
```

`config.json` está en `.gitignore`: **nunca lo subas**, contiene tu clave de Steam.

La clave se obtiene gratis en <https://steamcommunity.com/dev/apikey>.

---

## Estructura

| Archivo | Responsabilidad |
|---|---|
| `Program.cs` | Punto de entrada y despacho de comandos |
| `MainForm.cs` | Interfaz y coordinación |
| `Theme.cs` | Sistema visual y controles propios |
| `Rim.cs` | Rutas, `ModsConfig.xml`, inventario, registro |
| `Sorter.cs` | Grafo de orden de carga |
| `Health.cs` | Atribución de errores y diagnóstico |
| `Workshop.cs` | SteamCMD, actualizaciones, perfiles |
| `LanguageSystem.cs` | Enlaces de traducción |
| `AppConfig.cs` | Configuración |
| `GameFinder.cs` | Detección del juego y de SteamCMD |

---

## Limitaciones conocidas

- Solo Windows.
- Sin editor gráfico de reglas ni bases de datos comunitarias: usa lo que declaran
  los propios mods.
- La descarga por SteamCMD no muestra progreso en vivo.
- La atribución de errores es heurística: puede señalar un mod que solo envolvía la
  llamada. La columna dice «mencionados», no «causantes».

---

## Licencia

MIT. Ver [LICENSE](LICENSE).

No incluye contenido de RimWorld ni de ningún mod. RimWorld es propiedad de Ludeon
Studios; este proyecto no está afiliado ni respaldado por ellos.
