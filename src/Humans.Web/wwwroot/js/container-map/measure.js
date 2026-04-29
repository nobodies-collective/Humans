// Measuring tool: two-point distance with rubber-band preview.
// Standalone port of city-planning/measure.js — no appState dependency.

const EMPTY_FC = { type: 'FeatureCollection', features: [] };

let _map         = null;
let _active      = false;
let _firstPoint  = null;
let _secondPoint = null;
let _onMouseMove = null;

function formatDistance(meters) {
    if (meters >= 1000) return (meters / 1000).toFixed(2) + ' km';
    return Math.round(meters) + ' m';
}

function midpoint(a, b) {
    return [(a[0] + b[0]) / 2, (a[1] + b[1]) / 2];
}

function distanceLabel(a, b) {
    const meters = turf.distance(turf.point(a), turf.point(b), { units: 'meters' });
    return formatDistance(meters);
}

function setPoints(pts) {
    _map.getSource('measure-points').setData({
        type: 'FeatureCollection',
        features: pts.map(p => ({ type: 'Feature', geometry: { type: 'Point', coordinates: p }, properties: {} })),
    });
}

function setLine(a, b) {
    _map.getSource('measure-line').setData({
        type: 'FeatureCollection',
        features: [{ type: 'Feature', geometry: { type: 'LineString', coordinates: [a, b] }, properties: {} }],
    });
}

function setPreviewLine(a, b) {
    _map.getSource('measure-preview-line').setData({
        type: 'FeatureCollection',
        features: [{ type: 'Feature', geometry: { type: 'LineString', coordinates: [a, b] }, properties: {} }],
    });
}

function setLabel(pos, text) {
    _map.getSource('measure-label').setData({
        type: 'FeatureCollection',
        features: [{ type: 'Feature', geometry: { type: 'Point', coordinates: pos }, properties: { label: text } }],
    });
}

function clearAll() {
    _map.getSource('measure-points')?.setData(EMPTY_FC);
    _map.getSource('measure-line')?.setData(EMPTY_FC);
    _map.getSource('measure-preview-line')?.setData(EMPTY_FC);
    _map.getSource('measure-label')?.setData(EMPTY_FC);
}

function attachMouseMove() {
    detachMouseMove();
    _onMouseMove = e => {
        if (!_firstPoint) return;
        const cursor = [e.lngLat.lng, e.lngLat.lat];
        setPreviewLine(_firstPoint, cursor);
        setLabel(midpoint(_firstPoint, cursor), distanceLabel(_firstPoint, cursor));
    };
    _map.on('mousemove', _onMouseMove);
}

function detachMouseMove() {
    if (_onMouseMove) {
        _map.off('mousemove', _onMouseMove);
        _onMouseMove = null;
    }
}

function onMapClick(e) {
    const coord = [e.lngLat.lng, e.lngLat.lat];

    if (!_firstPoint) {
        _firstPoint = coord;
        _secondPoint = null;
        setPoints([_firstPoint]);
        _map.getSource('measure-line').setData(EMPTY_FC);
        _map.getSource('measure-label').setData(EMPTY_FC);
        attachMouseMove();
        return;
    }

    if (!_secondPoint) {
        _secondPoint = coord;
        setPoints([_firstPoint, _secondPoint]);
        setLine(_firstPoint, _secondPoint);
        setLabel(midpoint(_firstPoint, _secondPoint), distanceLabel(_firstPoint, _secondPoint));
        _map.getSource('measure-preview-line').setData(EMPTY_FC);
        detachMouseMove();
        return;
    }

    // Third click: start a new measurement from this point
    _firstPoint = coord;
    _secondPoint = null;
    setPoints([_firstPoint]);
    _map.getSource('measure-line').setData(EMPTY_FC);
    _map.getSource('measure-label').setData(EMPTY_FC);
    attachMouseMove();
}

export function enterMeasureMode() {
    if (_active) return;
    _active = true;
    _firstPoint = null;
    _secondPoint = null;
    clearAll();
    _map.getCanvas().style.cursor = 'crosshair';
    _map.on('click', onMapClick);

    const btn = document.getElementById('measure-btn');
    btn.classList.remove('btn-outline-secondary');
    btn.classList.add('btn-warning');
    btn.setAttribute('aria-pressed', 'true');
}

export function exitMeasureMode() {
    if (!_active) return;
    detachMouseMove();
    _map.off('click', onMapClick);
    clearAll();
    _firstPoint = null;
    _secondPoint = null;
    _active = false;
    _map.getCanvas().style.cursor = '';

    const btn = document.getElementById('measure-btn');
    btn.classList.remove('btn-warning');
    btn.classList.add('btn-outline-secondary');
    btn.setAttribute('aria-pressed', 'false');
}

export function isMeasuring() { return _active; }

export function initMeasure(map) {
    _map = map;
    map.addSource('measure-points',       { type: 'geojson', data: EMPTY_FC });
    map.addSource('measure-line',         { type: 'geojson', data: EMPTY_FC });
    map.addSource('measure-preview-line', { type: 'geojson', data: EMPTY_FC });
    map.addSource('measure-label',        { type: 'geojson', data: EMPTY_FC });

    map.addLayer({
        id: 'measure-line', type: 'line', source: 'measure-line',
        paint: { 'line-color': '#ff6600', 'line-width': 2, 'line-dasharray': [2, 2] },
    });
    map.addLayer({
        id: 'measure-preview-line', type: 'line', source: 'measure-preview-line',
        paint: { 'line-color': '#ff6600', 'line-width': 2, 'line-dasharray': [2, 2], 'line-opacity': 0.5 },
    });
    map.addLayer({
        id: 'measure-label', type: 'symbol', source: 'measure-label',
        layout: { 'text-field': ['get', 'label'], 'text-size': 13, 'text-anchor': 'center', 'text-allow-overlap': true },
        paint: { 'text-color': '#000000', 'text-halo-color': '#ffffff', 'text-halo-width': 2 },
    });
    map.addLayer({
        id: 'measure-points-stroke', type: 'circle', source: 'measure-points',
        paint: { 'circle-radius': 10, 'circle-color': '#fff' },
    });
    map.addLayer({
        id: 'measure-points', type: 'circle', source: 'measure-points',
        paint: { 'circle-radius': 7, 'circle-color': '#ff6600' },
    });
}
