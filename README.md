# Lodeon Admin

Gestor y diagnosticador de mods para **RimWorld 1.7** (motor Waylith, .NET 10).
Windows, WinForms, sin dependencias externas.

Nació para resolver un problema concreto que otros gestores no cubren: RimWorld solo
carga la carpeta de idioma cuyo nombre coincide **exactamente** con `langFolderName`
de `Prefs.xml`. Un mod que trae `Languages\Spanish` es ignorado si el juego espera
`Spanish (Español(Castellano))`. Lodeon Admin detecta esos casos y crea enlaces con el
nombre esperado.

---

## Descargar

No hace falta compilar. En [Releases](../../releases) hay dos paquetes:

| Archivo | Requisitos | Tamaño |
|---|---|---|
| `lodeon-admin-v1.6-portable.zip` | **Ninguno** (incluye el runtime) | ~63 MB |
| `lodeon-admin-v1.6.zip` | [.NET 8 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/8.0) | ~0.3 MB |

Descomprime el contenido dentro de la carpeta de RimWorld, junto a `RimWorld.exe`,
`Mods` y `Data`. Ejecuta `LodeonAdmin.exe`.

La primera vez detecta el juego solo y crea `config.json`. Si no lo encuentra, la
página **Ajustes** permite indicar la ruta a mano.

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

> No uses `PublishSingleFile=true`: genera un ejecutable que arranca sin interfaz y
> descarta los argumentos de consola. La version **autocontenida sin single-file** si
> funciona: se probo con los seis comandos y la interfaz.

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
| `-versiones` | Instalaciones de RimWorld encontradas en la carpeta de copias |
| `-ordercheck` | Comprueba que reactivar un mod respeta su posición original |
| `-coleccion <id>` | Dice si un id es un mod o una colección, y cuántos hijos tiene |
| `-vincular` | Propone ids del taller para los mods instalados a mano (`-aplicar` los escribe) |
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

## Orden de carga

Cada mod guarda su posición en `state/mod-order.json`. Al desactivar y volver a
activar un mod, **vuelve a su sitio** en lugar de irse al final.

Esto importa más de lo que parece: en RimWorld el orden decide si un parche se
aplica. Sin índice, reactivar un mod lo movía al final y sus parches podían dejar
de funcionar, con un fallo silencioso en la partida.

El índice se crea solo la primera vez y se actualiza al ordenar por reglas o al
cargar un perfil. `-ordercheck` lo verifica sin tocar nada.

## Varias versiones del juego

La página **Estado** tiene un botón **Versiones** que lista las copias guardadas
en la carpeta configurada y permite abrir cualquiera.

```
C:\Games\Versiones\1.6\
    RimWorld.exe  (o RimWorld\RimWorldWin64.exe)
    Version.txt
```

Para que una versión antigua no comparta partidas ni ajustes con la actual, la
ventana trae marcado **Usar datos separados**, que arranca el juego con
`-savedatafolder`.

## Sin clave de Steam

La clave de API **ya no hace falta** para lo esencial. La página del taller de Steam
es HTML público, así que se puede buscar por nombre, listar resultados y descargar.

| Función | ¿Necesita clave? |
|---|---|
| Buscar por nombre | No |
| Descargar e instalar | No |
| Buscar actualizaciones | No |
| Vincular mods al taller | No |
| Filtrar por etiqueta de versión (1.7) | Sí |

## Vincular mods instalados

Los mods descargados por Steam traen `About\PublishedFileId.txt`; los copiados a
mano no, y sin ese archivo no se les pueden comprobar actualizaciones.

El botón **Vincular instalados** busca cada uno por su título en el taller y
escribe el id. Solo acepta coincidencia **exacta**, y rechaza nombres genéricos.

Es deliberadamente conservador: escribir un id equivocado haría que una
actualización futura sustituyera el mod por otro completamente distinto. Se
prefiere no vincular antes que vincular mal.

## Colecciones del Workshop

Si pegas el id de una **colección** en Workshop, Lodeon pide sus hijos a la API y
descarga los mods uno a uno. SteamCMD no sabe descargar colecciones: sin esto,
pegarlas daba un error que no explicaba nada.

`-coleccion <id>` dice qué es un id sin abrir la ventana.

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
