# Lil Bombards — v0.1.0

Clon de **BombSquad** para Windows hecho con **Unity** (Built-in Render Pipeline) y **Blender**.
Esta es la base: el modo *Todos contra todos* (Deathmatch) con bots, multijugador local, **online**
y todas las mecánicas centrales de BombSquad.

> Nombre, código y arte son originales. No se usa ningún asset ni marca de BombSquad.

## Descargar y jugar (sin Unity)

1. Ve a **[Releases](https://github.com/AlfredoLino/lilbombards/releases/latest)** y descarga el ZIP
   (`LilBombards-v0.1.0-Windows.zip`).
2. Descomprímelo en una carpeta (clic derecho → *Extraer todo*). No lo abras desde dentro del ZIP.
3. Abre `LilBombards.exe`. Si sale "Windows protegió su PC": **Más información → Ejecutar de todas formas**.
4. Para jugar online con amigos, mira la sección [Online](#online).

Requisitos: Windows 10/11 de 64 bits y una tarjeta gráfica con DirectX 11.

---

El resto de este documento es para **desarrollar** el juego (abrir el proyecto en Unity, compilar, etc.).

## Requisitos

| Herramienta | Versión |
|---|---|
| Unity Hub + **Unity 6 LTS** (6000.x) | con el módulo *Windows Build Support (IL2CPP o Mono)* |
| Blender (opcional) | 4.x |

## Puesta en marcha

1. Unity Hub → **Add → Add project from disk** → elige esta carpeta (`lilbombards`) y la versión de Unity 6 instalada.
2. Al abrir, Unity instala los paquetes. Si pregunta por el **nuevo Input System** ("enable the new input backends?") responde **Yes** (se reinicia el editor).
3. El script de editor crea automáticamente `Assets/Scenes/Main.unity` y la añade al build
   (si no lo hace: menú **LilBombards → Configurar proyecto**).
4. Abre `Assets/Scenes/Main.unity` y pulsa **Play**. Todo el mundo (mapa, luces, cámara, HUD, sonidos) se genera por código.

### Generar el ejecutable de Windows (.exe)

#### 0. Antes de empezar (solo la primera vez)

1. Comprueba que tu Unity tiene el soporte para compilar en Windows:
   **Unity Hub → Installs →** engranaje de tu versión (6000.x) **→ Add modules**.
   - **Windows Build Support (Mono)** ya viene incluido con el editor de Windows. Con eso es suficiente.
   - **Windows Build Support (IL2CPP)** es opcional: hace el juego un poco más rápido, pero además necesita
     Visual Studio con la carga de trabajo *"Desarrollo para el escritorio con C++"*. Si no sabes cuál usar, usa Mono.
2. Abre el proyecto en Unity y espera a que termine de importar. Fíjate en la esquina inferior derecha:
   no debe quedar ninguna barra de progreso girando.
3. Abre la ventana **Console** (menú **Window → General → Console**) y comprueba que **no hay errores en rojo**.
   Con errores de compilación Unity no puede generar el .exe.
4. Si no existe `Assets/Scenes/Main.unity`, usa el menú **LilBombards → Configurar proyecto**.
   Crea la escena, la añade a la compilación y ajusta nombre, versión, pantalla completa, etc.

#### Opción A: con el menú del proyecto (recomendada)

1. En la barra de menús de Unity: **LilBombards → Compilar para Windows (x64)**.
2. Espera. La primera vez tarda varios minutos porque compila todos los shaders. Las siguientes son más rápidas.
   Mientras compila verás una ventana con una barra de progreso.
3. Al terminar se abre el Explorador en la carpeta del juego:

   ```
   lilbombards/Builds/Windows/
     LilBombards.exe            <- el juego
     LilBombards_Data/          <- datos (obligatoria)
     UnityPlayer.dll            <- motor (obligatorio)
     MonoBleedingEdge/          <- runtime de C# (obligatorio con Mono)
     UnityCrashHandler64.exe
     LilBombards_BurstDebugInformation_DoNotShip/   <- se puede borrar
   ```

4. En la Console aparece `[LilBombards] Build: Succeeded -> ...ruta...`. Si pone `Failed`, mira los errores en rojo
   justo encima.

#### Opción B: desde Build Profiles (lo estándar de Unity)

1. **File → Build Profiles** (en Unity 6; en versiones anteriores se llama *Build Settings*).
2. En la lista de la izquierda selecciona **Windows**. Si no es la plataforma activa, pulsa **Switch Platform**.
3. En **Scene List** debe aparecer marcada `Assets/Scenes/Main.unity`. Si no, ábrela y pulsa **Add Open Scenes**.
4. Architecture: **x86_64**. Deja *Development Build* **desmarcado** para la versión que vas a compartir.
5. Pulsa **Build** y elige una carpeta **vacía**, por ejemplo `Builds/Windows`. No elijas la raíz del proyecto ni `Assets`.

#### Probarlo

- Doble clic en `LilBombards.exe`. Arranca en pantalla completa a 1920×1080 (o la resolución de tu monitor).
  **Alt+Enter** alterna entre ventana y pantalla completa.
- La primera vez Windows puede mostrar:
  - **"Windows protegió su PC"** (SmartScreen), porque el .exe no está firmado: **Más información → Ejecutar de todas formas**.
  - El aviso del **Firewall** al crear o unirte a una partida online: marca **Redes privadas** y pulsa **Permitir acceso**.
- Para probar el online en una sola PC: abre el .exe y pulsa *Crear partida*. Luego abre **otra vez** el .exe
  (o dale Play en el editor) y únete a `127.0.0.1`.

#### Compartirlo con tus amigos

- **No mandes solo el .exe**: necesita la carpeta `LilBombards_Data`, `UnityPlayer.dll`, etc.
  Comprime **toda** la carpeta `Builds/Windows` (clic derecho → *Comprimir en archivo ZIP*). Puedes borrar antes
  `LilBombards_BurstDebugInformation_DoNotShip`.
- Quien lo reciba solo tiene que descomprimir el ZIP en una carpeta y abrir `LilBombards.exe`. No necesita instalar Unity.
- Para jugar online todos deben tener **la misma versión** (sale en el panel ONLINE, p. ej. `v0.1.0`).
- La carpeta `Builds/` está en `.gitignore`: el .exe **no** se sube al repositorio. Para publicarlo en GitHub usa
  **Releases**:
  1. En la página del repositorio: **Releases → Draft a new release** (o *Create a new release*).
  2. *Choose a tag*: `v0.1.0` (ya existe). Título: `Lil Bombards v0.1.0`.
  3. Arrastra el ZIP a *Attach binaries* y pulsa **Publish release**.

#### Al sacar una versión nueva

1. Sube la versión en `Assets/Scripts/Net/Net.cs` (`GameVersion = "0.2.0"`). Si cambias lo que se envía por red,
   sube también `Protocol`. Así no se mezclan versiones distintas en una partida.
2. Ejecuta **LilBombards → Configurar proyecto** para copiar la versión al Player, y compila de nuevo.
3. `git commit`, `git tag -a v0.2.0 -m "v0.2.0"`, `git push --tags` y crea la Release con el nuevo ZIP.

#### Problemas comunes

| Síntoma | Solución |
|---|---|
| No aparece el menú **LilBombards** | Hay errores de compilación: revisa la Console y corrígelos (o mándamelos). |
| "Build failed" / "Scripts have compiler errors" | Igual: errores en rojo en la Console. |
| "No Windows build module installed" o falta la plataforma Windows | Instala *Windows Build Support* desde Unity Hub (paso 0). |
| Error de IL2CPP o de Visual Studio | Usa Mono: **Edit → Project Settings → Player → Other Settings → Scripting Backend = Mono**. |
| El juego abre en negro o rosa | Recompila tras **LilBombards → Configurar proyecto**. Los shaders deben estar en `Assets/Resources/Shaders`. |
| Los amigos no pueden conectarse | Revisa la sección [Online](#online) (firewall, misma red/VPN o puerto 7777 abierto, misma versión). |
| Se borró la carpeta `Library/` | Es normal: Unity la regenera al abrir el proyecto (tarda unos minutos). |

### Modelos de Blender (opcional, mejora el aspecto)

El juego funciona sin modelos (usa primitivas de Unity). Para generar los modelos de Blender:

```
"C:\Program Files\Blender Foundation\Blender 4.x\blender.exe" --background --python Blender/generate_assets.py
```

Esto exporta FBX a `Assets/Resources/Models/` (cabeza, torso, pelvis, mano, zapato, bomba, mina, caja de powerup, TNT)
y guarda `Blender/lilbombards_assets.blend` para editarlos. Unity los detecta solos al volver al editor.
Convenciones: frente del objeto hacia **-Y** en Blender, un objeto por archivo; la escala la normaliza el juego.

## Controles

En la sala de espera, pulsa cualquier botón de acción para unirte. **ENTER / START** empieza la partida.

| Acción | Teclado 1 | Teclado 2 | Mando (Xbox) |
|---|---|---|---|
| Mover | WASD | Flechas | Stick izq. / cruceta |
| Saltar | Espacio | Num0 / Ctrl der. | A |
| Golpe | **Clic derecho** | Num1 / `,` | X |
| Bomba | **Clic izq.**: 1er clic la saca (la mecha empieza a arder); 2o clic mantener = cargar fuerza, soltar = lanzar hacia el puntero | Num2 / `.` (mantener = cargar, soltar = lanzar) | B (igual) |
| Agarrar / lanzar lo agarrado (mantener = cargar) | **Clic central** (rueda) | Num3 / `/` | Y |
| Correr (gasta estamina) | Shift izq. | Shift der. | Gatillos / hombros |

Sala de espera: `-`/`+` (o LB/RB) número de bots · RePág/AvPág (o cruceta arriba/abajo) eliminaciones para ganar ·
**TAB** / Shift+TAB (o cruceta izquierda/derecha, o las flechas del panel *MAPA*) cambiar de mapa · ESC salir.
En partida: ESC vuelve a la sala.

## Online

En la sala de espera hay un panel **ONLINE** arriba a la derecha.

### Por Internet: salas con código (recomendado)

- **Crear sala online**: el juego se conecta al servidor y te da un **código de 4 letras** (p. ej. `K7QX`).
  Pásaselo a tus amigos (*Copiar código*). Tú juegas normal y empiezas la partida con ENTER cuando estén todos.
- **Unirse**: escribe tu nombre y el código en *Código de sala* y pulsa *Unirse*. Se juega con teclado+ratón o mando
  y se puede entrar a mitad de partida. ESC (o *Desconectar*) te saca.
- Nadie tiene que abrir puertos ni instalar VPN: el servidor de relé (`Server/`) solo reenvía los mensajes
  entre el anfitrión y los demás.
- El campo *Servidor online* debe tener la dirección del servidor. Si la pones en `Net.DefaultServer`
  (ver [Desplegar el servidor](#desplegar-el-servidor-de-relé-con-dokploy)) antes de compilar el .exe,
  ya viene rellena para todos.

### En la misma red (LAN)

- **Crear en red local (LAN)**: el anfitrión abre el puerto TCP 7777 en su PC y el panel muestra sus IPs locales.
- Los demás escriben esa IP (`192.168.x.x`) en el mismo campo del código y pulsan *Unirse*.
- La primera vez Windows preguntará si permites el acceso a la red: acepta (al menos redes privadas).
- También funciona por Internet abriendo el puerto TCP 7777 en el router, o con una VPN tipo Tailscale/Radmin,
  pero con las salas con código no hace falta.

**Ping en pantalla** (esquina inferior derecha, en verde < 80 ms, amarillo < 150 ms, rojo más):
el cliente ve su ping con el anfitrión; el anfitrión ve su ping con el servidor y el de cada jugador.
Con salas por código el camino es *jugador → servidor → anfitrión*, así que el ping de un cliente es
aproximadamente *su ping al servidor + el del anfitrión al servidor*: conviene un VPS cercano a todos
(misma región/país) y que el anfitrión sea quien tenga mejor conexión (mejor por cable que por WiFi).

Funcionamiento: el anfitrión simula todo (física, bots, daño, desmembramiento) y envía ~30 instantáneas por
segundo; los clientes solo mandan sus controles y dibujan el mundo recibido. Los trozos que saltan del cuerpo
se calculan con la misma semilla en todos los equipos. Todos deben usar la **misma versión** del juego.
Código en `Assets/Scripts/Net/`.

### Desplegar el servidor de relé con Dokploy

El servidor está en `Server/` (C# / .NET 10, con `Dockerfile` y `docker-compose.yml`). Usa muy poca CPU y memoria.
Escucha en el puerto **TCP 7777**.

1. En Dokploy: **Projects → Create Project** (p. ej. `lilbombards`) → **Create Service → Compose**.
2. Pestaña **General**:
   - *Provider*: **GitHub** (conecta tu cuenta si no lo está) → repositorio `lilbombards`, rama `main`.
   - *Compose Path*: `./Server/docker-compose.yml`
   - Opcional: en *Watch Paths* pon `Server/**` para que solo se vuelva a desplegar cuando cambie el servidor
     (si no, cada `git push` del juego lo reinicia y corta las salas abiertas).
3. Pulsa **Deploy** y mira la pestaña **Logs**: debe aparecer `Relé de Lil Bombards escuchando en el puerto 7777`.
4. Abre el puerto en el firewall del VPS:
   ```
   sudo ufw allow 7777/tcp
   ```
   Si tu proveedor (Hetzner, Oracle, AWS…) tiene además un firewall en su panel web, abre también ahí **TCP 7777**.
5. Comprueba desde tu PC (PowerShell): `Test-NetConnection IP_DEL_VPS -Port 7777` → `TcpTestSucceeded : True`.
6. (Opcional) Un subdominio, p. ej. `lb.tudominio.com`, con un registro **A** a la IP del VPS. Si usas
   **Cloudflare**, ponlo en **DNS only** (nube gris): el proxy de Cloudflare no deja pasar TCP directo.
   Nota: aquí **no** hace falta configurar *Domains* en Dokploy (eso es para webs HTTP).
7. En el juego escribe esa dirección en *Servidor online* (`lb.tudominio.com` o la IP; otro puerto: `host:puerto`).
   Para que venga puesta en el .exe de tus amigos, edita `Assets/Scripts/Net/Net.cs`:
   ```csharp
   public const string DefaultServer = "lb.tudominio.com";
   ```
   y vuelve a compilar el juego.

Alternativa sin Compose: **Create Service → Application**, *Build Type* **Dockerfile**, *Docker File* `Dockerfile`,
*Docker Context Path* `Server`, y en **Advanced → Ports** publica `7777` → `7777` (TCP).

**Al actualizar el juego, redespliega primero el servidor** (Dokploy → *Deploy*): las versiones nuevas
pueden usar mensajes que el servidor antiguo no entiende (p. ej. la v0.3.1 envía una sola copia de cada
instantánea y el servidor la reparte; con el servidor antiguo los clientes no verían nada).

Límites del servidor (en `Server/Program.cs`): 500 salas, 16 jugadores por sala. Las salas se cierran solas cuando
el anfitrión se va.

## Qué incluye (fiel a BombSquad)

- **Personaje**: cabeza grande, manos y pies flotantes con física de muelles; corre (mantener cualquier botón), salta,
  golpea alternando manos (el daño crece con la velocidad), agarra y lanza bombas, cajas y **otros jugadores**.
- **K.O. tipo muñeco de trapo** al recibir golpes fuertes o explosiones; se levanta solo.
- **Daño por porcentaje (estilo Smash Bros)**: golpes y explosiones suman %; cuanto más % tienes, más lejos te lanzan y más cuesta soltarse de un agarre. Solo se muere al caer de la plataforma (el crédito es para el último que te golpeó).
- **Daño por trozos en capas**: cada pieza del cuerpo (casco ~100 trozos, peto ~70, cara ~60...) se divide en trozos orgánicos en sus 4 capas (armadura/ropa → piel → músculo → hueso). Cada explosión daña solo los trozos expuestos que miran hacia ella, según su orientación real y distancia; al romperse salen volando con su forma exacta y dejan ver la capa de debajo justo en ese sitio. Hollín y grietas se pintan por vértice. El hueso no se rompe, se chamusca. Modelos: `Blender/generate_body.py`; lógica: `BodyDamage.cs`.
- **Desmembramiento** (solo bombas, por el lado de la explosión; más fácil si las capas están destrozadas; pisar una mina arranca un pie): cada extremidad en 2 partes. Sin mano: lanza más cerca y tarda en sacar la bomba; sin brazo: el doble y no puede agarrar jugadores; sin brazos: no lanza, ni agarra, ni golpea. Sin pie: cojea; sin pierna: a gatas, sin saltar y quieto al lanzar; sin piernas: se arrastra con las manos. Sin ninguna extremidad: muere despedazado. Todo lo cortado queda en el campo (`CharacterLimbs.cs`).
- **Estamina** (100%): saltar 10%, correr 50%/s (2 s de sprint), golpear 8%, lanzar hasta 50% según la distancia (con poca estamina no puedes lanzar lejos), moverse con alguien agarrado 50%/s (agarrar impide recuperarla). Al agotarse caes de cansancio 0,5–2 s según tu %. Se recupera 30%/s.
- **Bombas**: normal (mecha), **hielo** (congela; los congelados se rompen al siguiente golpe), **pegajosa**, **impacto**,
  **minas** (se arman al tocar el suelo). Detonación en cadena. Máximo de bombas simultáneas (1, o 3 con Triple Bomba).
- **TNT** que explota con radio ×1.45 y reaparece.
- **Powerups** con las mismas probabilidades de BombSquad: Triple Bomba, Hielo, Pegajosas, Impacto, Minas,
  Guantes de boxeo, Escudo (650 HP), Salud y **Maldición** (explotas a los 5 s).
- **5 mapas** (se elige en la sala; el fondo cambia al momento para verlo):
  - **Puente de Bloques**: bloques de juguete sobre un mar de nubes, con barandas altas.
  - **Isla Tropical**: isla de hierba en el mar, con palmeras. Sin barandas.
  - **Tres Islas**: tres plataformas unidas por puentes estrechos, al atardecer.
  - **Coliseo de Lava**: arena cerrada por muros, de noche, con antorchas y un pozo de lava en el centro.
  - **Cumbre Nevada**: cima nevada con pinos, bloques de hielo y un muñeco de nieve para cubrirse.

  Caerse = morir. Cámara de ángulo fijo que sigue y hace zoom según la separación de los jugadores.
  Los bots conocen los bordes de cada mapa, cruzan por los puentes y rodean el pozo.
- **Bots** que persiguen, golpean, lanzan bombas con predicción, huyen de explosiones, recogen powerups y tiran rivales por el borde.
- Multijugador local (hasta 8: 2 teclados + mandos) y online (anfitrión + clientes), unirse en mitad de la partida.
- Efectos: bolas de fuego, chispas, humo, onda expansiva, marcas de quemado, temblor de cámara, cámara lenta al ganar.
- Sonidos sintetizados por código (sustituibles por archivos propios en `Sfx.cs`).

## Estructura

```
Assets/
  Scripts/
    Core/       GameManager (flujo y reglas), Tuning (todas las constantes), PlayerSlot
    Input/      Teclado x2, mandos, menús (Input System nuevo o clásico)
    Character/  LBCharacter (física y combate), CharacterVisual (cuerpo y animación), BotBrain (IA)
    Combat/     Bomb, Blast (explosiones), TntBox, PowerupBox, Pickupable
    World/      Arena (los 5 mapas: suelo, decorado, luz, cielo, bordes para los bots), CameraRig
    FX/         FX (partículas y efectos), Sfx (audio procedural)
    UI/         HUD (marcador, paneles de daño con silueta, mensajes)
    Net/        Online: Net (eventos), NetHost, NetClient, NetSnapshot, NetMenu
Server/         Servidor de relé para las salas online (C#, Docker / Dokploy)
    Util/       Gfx (materiales/texturas/modelos), MeshBuilder, Compat (API Unity 2021–6)
  Resources/Shaders/  LB/Toon (plástico brillante), Unlit, Particle, Shield, Sky, Water
  Resources/Models/   FBX exportados desde Blender
  Resources/Fonts/    Luckiest Guy (Apache 2.0)
  Editor/LBSetup.cs   Escena, build de Windows e importación de modelos
Blender/generate_assets.py
```

Para ajustar la sensación de juego, casi todo está en `Assets/Scripts/Core/Tuning.cs`.

## Próximos pasos sugeridos

- Más modos: Eliminación, Rey de la Colina, Captura la Bandera, Fútbol, Onslaught (cooperativo por oleadas), Runaround.
- Más mapas y mapas con elementos móviles.
- Selección de personaje/color en la sala, más personajes.
- Equipos, menú principal, opciones (volumen, pantalla), sonido y música con archivos reales.
- Ragdoll completo con articulaciones.
- Online: predicción en el cliente para reducir la sensación de latencia, lista de salas públicas.
