# Flujo Seurat → Unity a máxima calidad (reproducible)

Cómo pasar de un `.glb`/`.gltf` (o cualquier escena) a una escena Seurat que en
Unity se vea **igual que en el visor butterfly**, en este proyecto (Unity 6,
**URP**, color space **Linear**, target Android/Quest).

Resumen de las 3 claves que descubrimos (si algo se ve mal, es una de estas):
1. **Alpha recto, no premultiplicado** (`-premultiply_alpha=false` al hornear).
2. **Shader** `Seurat/AlphaBlendedLinearCorrect` (alpha recto, en `Assets/SeuratCapture/Shaders/`).
3. **Import de la textura**: sRGB ON, Alpha Is Transparency ON, Max Size 8192,
   Compression None, sin mipmaps.

---

## 1. Generar la captura (blink-cuda, en `c:\OneLink\blink-cuda`)

Desde el modelo. `--headbox_center` va **dentro** de una habitación (no fuera del
edificio); `--headbox_size` = 1 m = el volumen donde el visor puede moverse.

```bash
python tools/generate_capture_from_gltf.py \
  --glb _bench/appartement.glb \
  --output_dir _bench/capture-apt \
  --image_size 2048 \
  --num_view_groups 16 \
  --headbox_center -2 1.5 -2 \
  --headbox_size 1.0
```

(Para la escena sintética de prueba: `tools/generate_test_capture.py` con
`--image_size 2048 --num_view_groups 16`.)

## 2. Hornear la escena (máxima calidad + ALPHA RECTO)

**El `-premultiply_alpha=false` es lo que evita los "parches" y los "bordes
claros" en Unity.** El resto son los parámetros de calidad.

```bash
winbazel-bin/seurat/pipeline/seurat.exe \
  -input_path=_bench/capture-apt/manifest.json \
  -output_path=_bench/scene-apt-hq/scene \
  -premultiply_alpha=false \
  -triangle_count=144000 \
  -pixels_per_degree=16 \
  -texture_width=8192 \
  -texture_height=8192
```

Produce `scene.obj` (malla) y `scene.png` (atlas 8192²). El `.ice` y el `.exr`
NO se usan en Unity (el `.ice` es solo para butterfly).

> Palancas de calidad: `-pixels_per_degree` (densidad de textura) y
> `-triangle_count` (detalle de malla) suben calidad a costa de RAM/tiempo;
> `-texture_width/height` es el tamaño del atlas. Ojo: subir mucho ppd dispara
> los puntos del tiler y la RAM (a ppd 16 son ~50-60 M puntos; el equipo tiene
> 16 GB).

## 3. Importar a Unity

1. Copia `scene.obj` y `scene.png` a `Assets/Scenes/` (renómbralos si quieres,
   p.ej. `1.obj` / `1.png`).

2. **Textura `scene.png`** — Inspector:
   - sRGB (Color Texture): **ON**
   - Alpha Is Transparency: **ON**  (dilata el color al filtrar → sin bordes negros)
   - Generate Mip Maps: **OFF**  (evita sangrado entre tiles)
   - Wrap Mode: **Clamp**, Filter: **Bilinear**
   - Max Size: **8192**  (revisa la pestaña **Android** también; el default es 2048 y arruina el detalle)
   - Compression: **None** en Editor/PC. Para build de Quest: **ASTC 4×4** (memoria).
   - **Apply**.

3. **Malla `scene.obj`** — Inspector:
   - Scale Factor 1, Material Creation Mode: **None**, Normals/Tangents: None.
   - La malla sale centrada en el origen del headbox → ponla en `(0,0,0)` y
     coloca la cámara/headbox **dentro** de la malla (Seurat se ve desde el
     centro mirando hacia afuera).

4. **Material**:
   - Crea un Material, shader **`Seurat/AlphaBlendedLinearCorrect`**.
   - Asigna la textura a **_MainTex**.
   - Arrástralo a la malla.

Con esto se ve igual que butterfly.

---

## Si horneaste SIN `-premultiply_alpha=false` (atlas premultiplicado)

Los atlas premultiplicados dan parches (solapes oscuros) o bordes claros en un
proyecto Linear. Convierte el atlas a alpha recto una vez, con este script
(en `c:\OneLink\blink-cuda`), y usa el mismo shader:

```python
import numpy as np
from PIL import Image
Image.MAX_IMAGE_PIXELS = None
im = np.asarray(Image.open(r'_bench/scene-apt-hq/scene.png')).astype(np.float32)
rgb, a = im[:,:,:3], im[:,:,3]
amask = a > 0
straight = np.clip(rgb * np.where(amask, 255.0/np.maximum(a,1.0), 0.0)[:,:,None], 0, 255)
from scipy import ndimage                      # dilata color a los texels transparentes
_, (iy, ix) = ndimage.distance_transform_edt(~amask, return_indices=True)
filled = straight[iy, ix]; filled[amask] = straight[amask]
Image.fromarray(np.dstack([filled, a]).astype(np.uint8), 'RGBA').save(
    r'C:\OneLink\oneblink-seurat-unity\Assets\Scenes\1.png')
```

## Por qué (resumen técnico)

- El proyecto mezcla en **lineal** (correcto para URP/VR/PBR); butterfly mezcla
  en **gamma**. El alpha **premultiplicado en gamma** no se puede mezclar bien en
  lineal: o se oscurecen los solapes (parches) o se aclaran los bordes filtrados.
- **Alpha recto** evita el problema: `SrcAlpha·color + (1-alpha)·fondo` es
  correcto en lineal, y con la textura sRGB + dilatación los bordes quedan
  limpios. El shader `Seurat/AlphaBlendedLinearCorrect` hace exactamente eso
  (transparente, doble cara, `ZTest LEqual`, `ZWrite Off`).

## Nota VR / Quest

El shader es transparente (mismo trade-off de overdraw/sorting que cualquier
transparencia). Para el build de Quest, cambia solo la **compresión** de la
textura a **ASTC 4×4** (conserva el borde suave con ~64 MB en vez de ~256 MB
sin comprimir); el shader y el resto quedan igual.
