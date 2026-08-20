# Flujo Seurat completo: capturar en Unity → hornear → volver a Unity

Cómo pasar de una escena de Unity a una escena Seurat que en Unity se vea igual
que en el visor `butterfly`, con los ajustes medidos en el fork
[blink-cuda](https://github.com/Cristian-David-Araujo/blink-cuda) (ese repo
tiene el pipeline `seurat` + los generadores de captura y todas las mediciones).

Las tres claves, si algo se ve mal es una de estas:

1. **Sin antialiasing en la captura.** El plugin ya lo apaga (`Suppress
   Antialiasing` en el Capture Headbox). No lo desactives.
2. **Hornear con alpha recto** (`-premultiply_alpha=false`) si el proyecto está
   en color space **Linear** (URP/Quest lo están).
3. **Import del atlas**: sRGB ON, Alpha Is Transparency ON, Max Size = tamaño
   real del PNG (revisa también la pestaña Android), Compression None, sin
   mipmaps, Wrap Clamp.

---

## 1. Preparar la escena y el headbox

Añade el componente **Capture Headbox** a la cámara que va a capturar (requiere
un `Camera` en el mismo GameObject) y colócala **dentro** del volumen donde el
usuario podrá moverse.

| Campo | Qué hace | Recomendado |
|---|---|---|
| Headbox Size | El volumen navegable, en metros | 1 m por celda |
| Sample Count | View groups (posiciones de ojo capturadas) | 16 |
| Center Capture Resolution | Resolución de la vista central | 4x la de abajo |
| Default Resolution | Resolución del resto de las vistas | 1024–2048 |
| Dynamic Range | SDR escribe PNG, HDR escribe EXR | SDR |
| Suppress Antialiasing | Apaga MSAA + FXAA/SMAA/TAA de la cámara | ON |
| Suppress Post Processing | Apaga además todo el stack de post | ver abajo |

**`Default Resolution` fija el techo de la densidad de textura.** Una cara del
cubo abarca 90°, así que la captura entrega `resolution / 90` píxeles por grado:
1024 → 11, 2048 → 22. Pedirle al horno más que eso (`-pixels_per_degree`) no
inventa detalle, solo convierte el atlas sobrante en relleno de `InpaintSmooth`
(medido: a 768 px con PPD 20, el 49.4 % del atlas era relleno). El inspector
calcula el valor correcto en el desplegable **Seurat Bake Command**.

**`Sample Count` es cobertura, no nitidez.** Más view groups tapan huecos por
desoclusión, y a la vez el contorno de silueta visto desde el centro crece
monótonamente con ellos (medido 0.204 / 0.223 / 0.258 / 0.260 / 0.267 % para
1 / 2 / 4 / 8 / 16 view groups) mientras la media sobre cuatro ojos baja. Es
decir: el contorno es el precio de la cobertura multivista, y 16 es el
compromiso medido.

Cosas de la escena que sí importan:

- **Nada de materiales transparentes en lo que se captura.** El alpha de Seurat
  es binario (`ingest/ldi_loader.cc`: `alpha != 0` significa "hay muestra", no
  hay mezcla parcial), y la cámara de color mezcla el translúcido contra lo que
  hay detrás, así que ese píxel lleva un color que no pertenece a ninguna de las
  dos superficies. Medido en blink-cuda: una superficie translúcida hornea la
  superficie **opaca con el fondo pintado encima**. Usa opaco o alpha-tested.
- **El cielo no necesita geometría.** Los píxeles sin geometría salen con
  profundidad 0 y Seurat los interpreta como infinito y los clampa a la caja de
  `-skybox_radius` (200 por defecto). **Nunca hornees una captura de Unity con
  `-skybox_radius=0`**: esos píxeles se volverían muestras a distancia 0,
  delante del ojo.
- **Capa `SeuratProxy`**: los objetos en una capa con ese nombre se renderizan
  **solo en profundidad**. Sirve para poner una malla opaca alineada con algo que
  no puede escribir profundidad en el pase de reemplazo (p. ej. un gaussian
  splat) sin que aparezca en el color.
- **Captura en Edit mode**, no en Play con XR activo: `Camera.Render()` sobre una
  cámara estéreo renderiza los dos ojos dentro del render target.
- **`Suppress Post Processing`**: déjalo en OFF si quieres el look
  post-procesado horneado en el atlas. Ponlo en ON si la escena tiene bloom:
  el bloom reparte el color de un objeto brillante por encima de su silueta,
  que es exactamente el mismo defecto que causa el antialiasing.

Captura con el botón **Capture** del inspector. Desde línea de comandos:

```
Unity.exe -batchmode -projectPath <proyecto> -executeMethod SeuratBatchCapture.Run \
  -captureScene Assets/Scenes/MiEscena.unity -captureOutput C:\captura \
  -captureSamples 16 -captureResolution 1024 -quit
```

(No pases `-nographics`: la captura renderiza de verdad.)

El resultado es la carpeta que indique `Last Output Folder`: un `manifest.json`
y, por view group, 6 PNG/EXR de color y 6 EXR de profundidad.

## 2. Hornear

El desplegable **Seurat Bake Command** del inspector escribe el comando con los
valores medidos y con `-pixels_per_degree` ya derivado de la resolución. Es
este:

```
seurat -input_path=<captura>/manifest.json -output_path=<captura>/scene \
       -cache_path=<captura>/cache \
       -premultiply_alpha=false -specular_filter_size=2.0 -overdraw_factor=2 \
       -pixels_per_degree=11 -texture_width=8192 -texture_height=8192 \
       -triangle_count=144000
```

Produce `scene.obj` (malla) y `scene.png` (atlas). El `.ice` es solo para
`butterfly`.

Por qué cada flag:

- **`-premultiply_alpha=false`** — el atlas premultiplicado está premultiplicado
  en gamma, y Unity filtra y mezcla en lineal. Esa combinación no tiene arreglo
  limpio: o se oscurecen los solapes (parches) o se aclaran los bordes
  filtrados. Con alpha recto, `a·color + (1-a)·fondo` es correcto en lineal.
- **`-specular_filter_size=2.0`** — es la mayor reducción medida del halo
  claro alrededor de los objetos. `radiance_accumulator.cc` pesa cada muestra
  con `exp(-0.5 · radio_ojo / sigma²)` y un piso `kMinWeight = 1e-6`; con el
  default 0.05 ese piso se alcanza a pocos centímetros del centro, así que el
  color sale **de una sola vista** y el halo aparece en cuanto el ojo se mueve
  (medido: 449 → 1280 píxeles de halo en 5 cm). Con 2.0 se promedian las 16
  vistas: el halo baja ~40 % y deja de crecer al moverse. El costo es el que
  documenta el flag: los reflejos se vuelven difusos.
- **`-overdraw_factor`** — 2 fue el mejor medido sobre geometría exacta (mejor
  en todas las métricas con la mitad de triángulos), pero por debajo de 3 el
  presupuesto de triángulos deja de ser el límite y la escena puede quedar
  escasa (a 144 k de presupuesto, overdraw 1.0/1.5/2.0/3.0 selecciona
  850 / 9 384 / 65 390 / 129 930 triángulos). Si la malla sale pobre, vuelve al
  default 3.
- **`-cache_path`** — reutiliza la etapa de geometría. Cambiar textura,
  `-pixels_per_degree`, `-ray_footprint` o `-specular_filter_size` re-hornea en
  minutos; cambiar `-triangle_count` o `-overdraw_factor` invalida el caché.

Lo que **no** funciona, ya medido, para no volver a intentarlo:

- Subir `-triangle_count` cuando lo que ata es el overdraw: 300 k selecciona los
  mismos 61 826 triángulos y emite un `.obj`/`.png` **bit a bit idéntico**.
- `-silhouette_erode` y `-freespace_color_weight` mueven el **signo** del error
  de silueta (+0.153 → −0.051) dejando su **magnitud** plana (0.205–0.218), y
  `-freespace_color_weight=1.0` sale peor al contar los píxeles que reemplaza.
- El contorno que queda es **estructural**: la silueta se mueve con el paralaje
  pero Seurat le da al objeto un plano y una máscara de alpha para todo el
  headbox, y el resolve toma la unión de las siluetas que vio cada view group.
  Ningún valor almacenado de alpha lo gana (se probó, incluido el óptimo por
  texel). Lo único que reduce su magnitud es un headbox más chico (−29 % de 1.0
  a 0.5 m).

## 3. Importar a Unity

1. Copia `scene.obj` y `scene.png` a `Assets/`.

2. **Textura** (`scene.png`) — Inspector:
   - sRGB (Color Texture): **ON**
   - Alpha Is Transparency: **ON** (dilata el color al filtrar → sin bordes
     negros). Con atlas **premultiplicado** va al revés: OFF.
   - Generate Mip Maps: **OFF** (evita sangrado entre tiles del atlas)
   - Wrap Mode: **Clamp**, Filter: **Bilinear**, Aniso: 1
   - Max Size: el tamaño real del PNG (8192 si horneaste a 8192). **Revisa la
     pestaña Android**: su default de 2048 arruina el detalle en el build.
   - Compression: **None** en Editor/PC; **ASTC 4×4** para el build de Quest.
   - Apply.

3. **Malla** (`scene.obj`) — Scale Factor 1, Material Creation Mode None,
   Normals/Tangents None. Sale centrada en el origen del headbox: ponla en
   `(0,0,0)` y deja la cámara **dentro** (Seurat se mira desde el centro hacia
   afuera). En Player Settings → Android → Vertex Compression, **desactiva la
   compresión del canal UV0**, o aparecen grietas.

4. **Material** — shader **`Seurat/AlphaBlendedLinearCorrect`**
   (`Shaders/SeuratLinearCorrect.shader`) con el atlas en `_MainTex`. Es el
   shader para alpha recto en un proyecto Linear con URP. Si horneaste con alpha
   premultiplicado, usa `GoogleVR/Seurat/AlphaBlended`.

Con esto se ve igual que `butterfly`.

## Si horneaste sin `-premultiply_alpha=false`

Puedes convertir el atlas a alpha recto una vez, en lugar de re-hornear (los
texels transparentes se rellenan con el color del vecino más cercano para que el
filtrado bilineal no traiga negro):

```python
import numpy as np
from PIL import Image
from scipy import ndimage
Image.MAX_IMAGE_PIXELS = None

im = np.asarray(Image.open('scene.png')).astype(np.float32)
rgb, a = im[:, :, :3], im[:, :, 3]
opaque = a > 0
straight = np.clip(rgb * np.where(opaque, 255.0 / np.maximum(a, 1.0), 0.0)[:, :, None], 0, 255)
_, (iy, ix) = ndimage.distance_transform_edt(~opaque, return_indices=True)
filled = straight[iy, ix]
filled[opaque] = straight[opaque]
Image.fromarray(np.dstack([filled, a]).astype(np.uint8), 'RGBA').save('scene_straight.png')
```

## Nota VR / Quest

El shader del atlas es transparente, con el mismo compromiso de overdraw y
ordenamiento que cualquier transparencia. Para el build de Quest solo cambia la
**compresión** de la textura a ASTC 4×4 (conserva el borde suave con ~64 MB en
vez de ~256 MB); shader y malla quedan igual.
