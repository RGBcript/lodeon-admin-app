# RUTA — Depuración y cierre de Lodeon Admin

Creado: 2026-10-22 · Estado de partida: v1.3

---

## Objetivo

Dejar Lodeon Admin sin cosas de más, con la información en pantalla explicada y con
las funciones que faltan para considerarlo completo. Sin añadir adornos.

---

## Fase 0 — Inventario honesto (antes de tocar nada)

Revisar qué hay en la interfaz y decidir, elemento por elemento:

| Pregunta | Criterio |
|---|---|
| ¿Aporta algo que el usuario use? | Si no, fuera |
| ¿Se explica solo? | Si no, añadir una línea de texto |
| ¿Está duplicado en otra página? | Unificar |
| ¿Es herramienta de desarrollo? | Mover a consola, no en la UI |

Entregable: lista de lo que se queda, lo que se va y lo que necesita explicación.

---

## Fase 1 — Quitar lo que sobra

Candidatos detectados en el análisis anterior:

- [x] **Selector de 29 idiomas** → ahora solo muestra los que tienen **2 o más
      mods** (16 en total). Bajo la tabla aparece «+ 10 idioma(s) presentes en
      un solo mod, no listados arriba».
- [x] **Cuarentena de ruso** → dejada como está (ya cumplió con 235 carpetas);
      no se destaca, vive dentro de su bloque en Idioma.
- [ ] **Botones de consola en la UI** si alguno asoma (`-bridge`, `-langreport`).
- [ ] **`_page` / `_busy`** y cualquier campo que solo exista para el temporizador.
- [x] **`Fade` eliminada** — eran 45 líneas que nunca se instanciaban.

---

## Fase 2 — Explicar lo que no se entiende

Puntos donde el usuario puede preguntarse «¿esto qué hace?»:

- [x] **Tarjeta «Registro»** — ya muestra «Limpio» o el número, con «errores en el log».
- [x] **Columna «1.7»** — la cabecera del listado lo indica.
- [x] **«Enlaces de idioma»** — la fila de Mods ya explica: «Vuelve a crear los
      puentes de traducción si se perdieron».
- [x] **«Comprobables»** — el mensaje dice «N de M comprobables» y avisa si
      ninguno tiene ID del Workshop.
- [x] **Ordenar por reglas** — la fila explica: «Coloca cada mod según sus
      loadAfter/loadBefore».
- [x] **Perfiles** — el diálogo dice «Se activarán los mods de…».

---

## Fase 3 — Completar lo que falta

Pendientes reales tras v1.3:

- [ ] **Buscar actualizaciones** en la interfaz, con botón para actualizar lo elegido.
- [x] **Aviso al arrancar** — comprueba en segundo plano tras 3 s, avisa una vez
      al día y se puede apagar en Workshop («Aviso al arrancar: sí/no»).
- [x] **`ModsConfig.xml` inválido** — detecta XML roto, bloque `activeMods`
      ausente o lista vacía, y ofrece restaurar la copia más reciente guardando
      el archivo actual como `.antes-de-restaurar`.
- [x] **Mod sin `About.xml`** — aparece en la lista en gris con «(sin About.xml)»,
      no se puede marcar y el contador lo informa.
- [x] **Juego abierto** — al aplicar la lista, al ordenar y al cargar un perfil se
      avisa de que hay que reiniciar RimWorld.
- [x] Todas las acciones destructivas piden confirmación (verificado en código).

---

## Fase 4 — Verificación

Por cada cambio, la prueba mínima:

| Cambio | Prueba |
|---|---|
| Elemento quitado | La página sigue usable y no queda hueco |
| Texto añadido | Se lee completo, sin cortarse, con la ventana mínima |
| Función nueva | Ejecutada de verdad, con evidencia del efecto |
| Todo junto | `-selftest` OK + captura de las 5 páginas |

---

## Fase 5 — Publicación

- [x] Compilado en Release
- [x] `-selftest`, `-sortreport` y `-langreport` en verde
- [x] Paquete completo copiado
- [x] **`REA.md` y `RUTA.md` preservados** — se leen en memoria antes de borrar
      y se reescriben después. Ya están en el paquete.
- [x] Acceso directo actualizado
- [x] Verificado por captura de las 5 páginas

---

## Regla de trabajo

Un cambio, una verificación. Si algo rompe, se arregla antes de seguir con lo siguiente.
Y nada se da por bueno sin haberlo ejecutado.

---

## Nota aparte: MCP rea

Reactivado en `cordis.patch.yml` el 2026-10-22. **Requiere reiniciar el host** para que
el arnés lo cargue; hasta entonces las herramientas `rea_*` no aparecen.

---

## Estado al cerrar esta sesión

```
Version .......... v1.3
Autotest ......... OK
Ordenador ........ 807/807 reglas, 0 avisos
Idiomas .......... 16 visibles de 29 (+10 de un solo mod)
Paquete .......... exe 206 KB + dll 182 KB + deps + runtimeconfig + assets
Documentos ....... REA.md y RUTA.md dentro del paquete
```

### Pendiente real para otra sesion

- [x] **Aviso al arrancar** — comprueba en segundo plano tras 3 s, avisa una vez
      al día y se puede apagar en Workshop («Aviso al arrancar: sí/no»).
- [x] **`ModsConfig.xml` inválido** — detecta XML roto, bloque `activeMods`
      ausente o lista vacía, y ofrece restaurar la copia más reciente guardando
      el archivo actual como `.antes-de-restaurar`.
- [x] **Mod sin `About.xml`** — aparece en la lista en gris con «(sin About.xml)»,
      no se puede marcar y el contador lo informa.
- [x] **MCP `rea`** activo y verificado (v6.7.0, 207 herramientas).
---

## Detalles visuales corregidos

| Síntoma | Causa | Arreglo |
|---|---|---|
| Texto pequeño suelto **arriba** del panel Idioma | `lblOcultos` estaba en `y=0` en `LayoutPages` | Se calcula bajo la tabla: `166 + altoLista + 8` |
| «Usar este» / «Elegir por mí» no se veían | Se reposicionaban en `RefrescarIdioma` con `lista.Bottom`, antes de que el layout existiera | Posiciones fijas en `LayoutPages`, código frágil eliminado |

## Verificación final

```
Autotest ......... RESULTADO: OK
Ordenador ........ 807/807 reglas · 0 avisos · 405 mods
Config ........... Ok · 405 mods · 18 copias
Idioma ........... texto bajo la tabla · botones visibles · v1.3
Lista ............ fila sin About.xml en gris, casilla bloqueada
```

## Corrección de una medición propia

Al contar carpetas sin `About.xml` con PowerShell reporté **201**. Era falso: los
corchetes de nombres como `[RJW]` funcionan como comodines en `Test-Path`. Con
`-LiteralPath` el resultado real es **404 con About.xml y 0 sin**. El código C# nunca
tuvo ese problema porque usa `Path.Combine`.
---

## v1.4 — Salud y empaquetado del repo

### Funciones nuevas

**Página Salud** — un solo informe con lo que puede ir mal:

- **Atribución de errores a un mod.** RimWorld no escribe el nombre del mod en la
  línea de error, pero las trazas llevan marcos `at Namespace.Clase(...)`. Ese
  namespace se cruza con tres índices: ensamblados instalados, segmentos del
  `packageId` y nombres de carpeta normalizados. Cobertura medida: **771 de 778
  marcos atribuidos (99%)**, 44 mods.
- Solo el **primer** mod de cada traza recibe la entrada; los demás solo suman
  marcos. Así no aplasta al resto un mod que solo envolvía la llamada.
- Dependencias rotas, incompatibilidades, reglas de orden incumplidas.
- Cruce «declara solo hasta 1.6 **y** aparece en el registro»: sin el cruce salían
  262 filas inútiles; con él, 3.
- Botón para **desactivar** el mod señalado sin salir de la página.

**Comando `-health`** en consola, con código de salida.

### Empaquetado del repo

```
Carpeta antes: 150,6 MB   →   ahora: 244 KB
```

| Archivo | Para qué |
|---|---|
| `.gitignore` | Excluye bin, obj, publish, config.json, perfiles e informes |
| `README.md` | Qué hace, cómo compilar, comandos, configuración, límites |
| `LICENSE` | MIT |

Rutas con el nombre del usuario eliminadas de `AppConfig.cs`.

**Repo git creado**, rama `master`:

```
2fd19d1  Aclarar el contador de mods en disco frente a activos
e5d5023  Lodeon Admin 1.4: gestor y diagnostico de mods para RimWorld 1.7
```

### Verificación

```
autotest .... OK
salud ....... SANO
sortreport .. OK
configcheck . OK
clave Steam . ausente de la carpeta (244 KB, 0 coincidencias)
```

### Corrección anotada

La tarjeta de Inicio decía «405 activos / 404 instalados» sin explicar por qué.
La lista activa incluye `ludeon.rimworld` y los 5 DLC, que no tienen carpeta en
`Mods`. Ahora el apoyo dice «404 en disco».
---

## v1.5 — Fluidez de descarga y estructura

### El defecto real de fluidez

Nuestro codigo lanzaba **un proceso SteamCMD por mod**. Cada uno arranca el motor,
hace login anonimo, descarga y se cierra.

Medido: **2 mods en 8,3 s**. Con 20 mods serian ~80 s, casi todo en arranques.

SteamCMD acepta **varios `+workshop_download_item` en una sola invocacion**,
reutilizando el login. Medido ahora: **3 mods en 12,5 s con un solo proceso**.
La ganancia crece con la cantidad.

No hizo falta ingenieria inversa: WorkshopDL usa el mismo motor, y la mejora
estaba en la propia documentacion de SteamCMD.

### Cambios

- **`Workshop.Descargar`** arma un unico `ProcessStartInfo` con todos los IDs.
- **Progreso en vivo**: callback por linea, con `BeginInvoke` para volver al hilo
  de interfaz. Antes el cuadro decia «puede tardar varios minutos» y no se movia.
- Un item que ya estaba descargado (`Validating active workshop item`) cuenta
  como disponible, no como fallo.
- Los errores se deduplican y se muestran al final.

### Refactor

`MainForm.cs` paso de **1.983 a 1.099 lineas**:

| Archivo | Lineas |
|---|---|
| `MainForm.cs` | 1.099 |
| `MainForm.Idioma.cs` | 522 |
| `MainForm.Salud.cs` | 200 |
| `MainForm.Mantenimiento.cs` | 186 |

82 miembros en total, clase `partial`. Sin cambios de comportamiento.

### Verificacion

```
selftest .... OK
health ...... SANO
sortreport .. OK
configcheck . OK
UI .......... 29 controles presentes (sidebar, botones, tabla)
cache ....... 172 items, igual que al empezar
```

### Errores propios de esta sesion

1. Al partir el archivo con `-join` sobre un `foreach` se aplastaron las lineas en
   una sola. Restaurado con `git checkout` y rehecho con slicing de arrays.
2. Un heredoc de PowerShell rompio el escapado de `\n` dentro de literales C#.
   Corregido con `edit`.
3. Descargue un item de prueba que no existia (`818773962`, 2,3 MB) y lo elimine
   despues. Cache confirmado en 172.
---

## Diagnostico de secciones (v1.5)

Se lanzo cada pagina con `-page`, se capturo stderr y stdout, y se reviso el
event log de Windows. **Cero excepciones en las 6 secciones.**

| Seccion | Estado | Nota |
|---|---|---|
| Inicio | OK | 4 tarjetas, detalle tecnico, sin solapamientos |
| Salud | OK | veredicto + 9 filas utiles + 3 botones |
| Workshop | OK | 29 controles, aviso de clave ausente correcto |
| Mods | OK | 7 filas (dibujadas con GDI+, no expuestas a UI Automation) |
| Lista | **corregido** | el contador estaba fuera de la ventana |
| Idioma | OK | 16 idiomas, linea de ocultos, botones visibles |

### Defectos encontrados y corregidos

1. **Contador de Lista invisible.** `_lblLista` estaba en `x=760` con ancho 220
   fijos: con la ventana a 1020 px se salia del area util. Ademas `lblLista` y
   `filtro` **no tenian entrada en `LayoutPages`**, asi que nunca se recalculaban.
   Ahora ambos se posicionan de forma relativa al ancho disponible.

2. **Linea ruidosa en el veredicto de Salud.** "Sin problemas detectados" iba
   seguido de "262 mods declaran solo hasta 1.6 (no es un problema)". Se quito:
   un veredicto limpio no necesita explicar lo que no es un problema.

### Falsa alarma investigada

La pagina Mods parecia vacia a UI Automation (solo 12 elementos). **No era un
defecto**: `ActionRow` se dibuja con GDI+ y no crea controles hijos, por lo que
no aparece en el arbol de accesibilidad. Verificado por captura: las 7 filas
estan completas y visibles.

### Integridad de datos

`ModsConfig.xml` se respaldo antes del diagnostico y se comparo despues:
**identico, 405 mods activos**.
---

## v1.7 — recuperar lo que se perdio en la reescritura

Comparando la version PowerShell (`dsh-tools\lodeonadmin`) con la C# aparecieron
cuatro funciones que la reescritura no traia. Tres ya estan.

### Orden de carga persistente (lo importante)

El lanzador original guardaba la posicion de cada mod en `state/mod-order.json`.
La version C# no: al desactivar y reactivar un mod lo añadia al final de
`ModsConfig.xml`.

Consecuencia real: en RimWorld el orden decide si un parche se aplica. Mover un
mod podia desactivar sus parches, y el fallo es silencioso — no hay error, solo
cosas que dejan de funcionar.

Ahora `Order.cs` mantiene el indice y `-ordercheck` lo demuestra:

```
Al final (antes)  : 101/405 posiciones intactas
Reinsertar (ahora): 405/405 posiciones intactas
```

### Selector de versiones

`VersionsFolder` estaba en la configuracion pero no se usaba. Ahora el boton
**Versiones** de la pagina Estado lista las copias guardadas y abre cualquiera,
con la opcion de usar datos separados (`-savedatafolder`) para no mezclar
partidas ni ajustes entre versiones.

### Colecciones del Workshop

Pegar el id de una coleccion fallaba con un error de SteamCMD que no explicaba
nada, porque SteamCMD no descarga colecciones. Ahora se piden los hijos a la API
y se descargan uno a uno.

### Pendiente de la comparacion

- Vincular un mod instalado a mano con su id del Workshop (`Find-WorkshopIdForMod`
  del original): sirve para poder comprobarle actualizaciones.

### Arreglo de paso

Un argumento de consola desconocido abria la ventana y colgaba el script. Ahora
avisa y lista los comandos validos. `-page` se exceptua porque abre la interfaz
a proposito.
---

## v1.8 — buscar en el taller sin clave de API

Sospechaba que la clave de Steam no era necesaria para todo, y era cierto: la
pagina publica del taller devuelve HTML con los resultados y sus titulos.

```
https://steamcommunity.com/workshop/browse/?appid=294100&searchtext=...&browsesort=textsearch
```

Devuelve 30 resultados por pagina con `id` y `titulo` en el `alt` de la miniatura.
Comprobado con varias busquedas; los ids coinciden con los de la API oficial.

### Que deja de necesitar la clave

| Funcion | Antes | Ahora |
|---|---|---|
| Buscar por nombre | exigia clave | funciona sin clave |
| Vincular mods al taller | exigia clave | funciona sin clave |
| Descargar e instalar | ya no la usaba | igual |
| Filtrar por etiqueta 1.7 | exigia clave | sigue exigiendo clave |

### Vincular instalados

`BuscarVinculos` recorre los mods sin `About\PublishedFileId.txt` (en este equipo
son 61) y busca cada uno por su titulo. Si encuentra coincidencia exacta escribe
el id, y entonces ya se le pueden comprobar actualizaciones.

**El peligro que se encontro.** La primera version casaba por titulo normalizado y
daba por buena esta coincidencia:

```
[RJW] Insects  (Ed86, packageId Ed86.rjwinsects)
   -> id 847342844 = "Insects!" de 2017, otro mod completamente distinto
```

Un id equivocado haria que una actualizacion futura sustituyera el mod por otro.
Ahora se exige ademas:

- nombre de al menos 12 letras y dos palabras (los genericos como "Genes" no valen)
- coincidencia exacta de la clave normalizada
- si hay varios candidatos, desempate por titulo literal y luego por suscriptores
  reales; si sigue habiendo empate, no se vincula

Probado con mods que si estan en Steam copiados a mano:

```
Dubs Bad Hygiene            -> 836308268   correcto
Vanilla Expanded Framework  -> 2023507013  correcto
[RJW] Animal Graphics Addon -> sin vinculo (no esta en Steam)
```

### De paso

- El aviso decia "Workshop desactivado" sin clave, y era falso: ya solo falta el
  filtro por etiqueta.
- El boton "Descargar e instalar" se salia por la derecha de la ventana.