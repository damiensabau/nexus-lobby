# Convertit le TopoJSON world-atlas en géométries XAML (projection équirectangulaire) :
# une géométrie pour le monde entier + une par pays du catalogue (clé « Country.XX »).
import json
import sys

src, dst = sys.argv[1], sys.argv[2]
W, LAT_TOP, LAT_BOTTOM = 1000.0, 84.0, -58.0
H = W * (LAT_TOP - LAT_BOTTOM) / 360.0

# Nom Natural Earth -> code ISO alpha-2 (pays du catalogue de l'app).
CODES = {
    'France': 'FR', 'United Kingdom': 'GB', 'Germany': 'DE', 'Netherlands': 'NL', 'Belgium': 'BE',
    'Switzerland': 'CH', 'Spain': 'ES', 'Portugal': 'PT', 'Italy': 'IT', 'Ireland': 'IE', 'Sweden': 'SE',
    'Norway': 'NO', 'Finland': 'FI', 'Poland': 'PL', 'Romania': 'RO', 'Turkey': 'TR', 'Ukraine': 'UA',
    'United States of America': 'US', 'Canada': 'CA', 'Mexico': 'MX', 'Brazil': 'BR', 'Argentina': 'AR',
    'Chile': 'CL', 'Colombia': 'CO', 'Peru': 'PE', 'South Africa': 'ZA', 'Egypt': 'EG', 'Morocco': 'MA',
    'Nigeria': 'NG', 'Kenya': 'KE', 'Ghana': 'GH', 'United Arab Emirates': 'AE', 'Saudi Arabia': 'SA',
    'Israel': 'IL', 'India': 'IN', 'Pakistan': 'PK', 'Bangladesh': 'BD', 'Kyrgyzstan': 'KG',
    'Vietnam': 'VN', 'Cambodia': 'KH', 'Japan': 'JP', 'South Korea': 'KR', 'Taiwan': 'TW',
    'Thailand': 'TH', 'Indonesia': 'ID', 'Philippines': 'PH', 'Australia': 'AU', 'New Zealand': 'NZ',
}

# Territoires d'outre-mer exclus du surlignage, selon la longitude minimale du contour.
EXCLUDE = {'FR': lambda lon: lon < -20}

topo = json.load(open(src, encoding='utf-8'))
sx, sy = topo['transform']['scale']
tx, ty = topo['transform']['translate']
arcs = []
for arc in topo['arcs']:
    x = y = 0
    pts = []
    for dx, dy in arc:
        x += dx
        y += dy
        pts.append((x * sx + tx, y * sy + ty))
    arcs.append(pts)


def arc_points(i):
    return arcs[i] if i >= 0 else list(reversed(arcs[~i]))


def ring(indices):
    pts = []
    for i in indices:
        p = arc_points(i)
        pts.extend(p if not pts else p[1:])
    return pts


def proj(lon, lat):
    return (lon + 180) / 360 * W, (LAT_TOP - lat) / (LAT_TOP - LAT_BOTTOM) * H


def ring_path(r):
    pts = ring(r)
    lons = [p[0] for p in pts]
    if max(lons) - min(lons) > 180:  # traverse l'antiméridien
        pts = [(lon + 360 if lon < 0 else lon, lat) for lon, lat in pts]
    out, last = [], None
    for lon, lat in pts:
        x, y = proj(lon, lat)
        q = (round(x, 1), round(y, 1))
        if q != last:
            out.append(q)
            last = q
    if len(out) < 3:
        return ''
    return 'M' + ' '.join(f'{x:g},{y:g}' for x, y in out) + 'Z'


world = []
countries = {}
for g in topo['objects']['countries']['geometries']:
    if g.get('id') == '010':  # Antarctique
        continue
    polys = g['arcs'] if g['type'] == 'MultiPolygon' else [g['arcs']]
    paths = ''.join(ring_path(r) for poly in polys for r in poly)
    world.append(paths)
    code = CODES.get(g['properties']['name'])
    if code:
        # Surlignage : on ignore les territoires lointains (ex. Guyane pour la France).
        keep = [poly for poly in polys if not EXCLUDE.get(code, lambda lon: False)(min(p[0] for p in ring(poly[0])))]
        countries[code] = ''.join(ring_path(r) for poly in keep for r in poly)

with open(dst, 'w', encoding='utf-8') as f:
    f.write('<ResourceDictionary xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"\n'
            '                    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">\n'
            '    <!-- Généré depuis Natural Earth 1:110m (world-atlas@2), domaine public. '
            f'Projection équirectangulaire {int(W)}x{H:.1f}, latitudes {LAT_TOP:g} à {LAT_BOTTOM:g}. -->\n'
            f'    <Geometry x:Key="WorldMapGeometry">{"".join(world)}</Geometry>\n'
            '    <!-- Un pays par clé « Country.XX » (code ISO alpha-2), pour le surlignage. -->\n')
    for code, data in sorted(countries.items()):
        f.write(f'    <Geometry x:Key="Country.{code}">{data}</Geometry>\n')
    f.write('</ResourceDictionary>\n')

print('pays surlignables :', len(countries), '- manquants :', sorted(set(CODES.values()) - set(countries)))
