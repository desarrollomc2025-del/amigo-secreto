// Amigo Secreto: mejoras progresivas (la app funciona sin JavaScript).
(() => {
    'use strict';

    const CLAVE = 'amigoSecreto.codigo';
    const almacen = {
        leer() { try { return localStorage.getItem(CLAVE); } catch { return null; } },
        guardar(v) { try { localStorage.setItem(CLAVE, v); } catch { /* modo privado */ } },
        borrar() { try { localStorage.removeItem(CLAVE); } catch { /* modo privado */ } },
    };
    const sinMovimiento = window.matchMedia('(prefers-reduced-motion: reduce)').matches;

    // Guardar el código mostrado en la página y rellenarlo donde se pida.
    document.querySelectorAll('[data-save-code]').forEach(el => {
        if (el.dataset.saveCode) almacen.guardar(el.dataset.saveCode);
    });
    document.querySelectorAll('input[data-autofill-code]').forEach(input => {
        const guardado = almacen.leer();
        if (!input.value && guardado) input.value = guardado;
    });

    document.addEventListener('click', event => {
        const objetivo = event.target instanceof Element ? event.target : null;
        if (!objetivo) return;

        // Confirmación antes de acciones delicadas.
        const conConfirmacion = objetivo.closest('[data-confirm]');
        if (conConfirmacion && !window.confirm(conConfirmacion.dataset.confirm)) {
            event.preventDefault();
            return;
        }

        const copiar = objetivo.closest('[data-copy]');
        if (copiar) { copiarTexto(copiar); return; }

        if (objetivo.closest('[data-forget-code]')) { almacen.borrar(); return; }

        const regalo = objetivo.closest('[data-gift]');
        if (regalo) abrirRegalo(regalo);
    });

    // Evita envíos dobles (p. ej. doble toque en el celular).
    document.addEventListener('submit', event => {
        const form = event.target;
        if (form.dataset.enviando) { event.preventDefault(); return; }
        form.dataset.enviando = '1';
        setTimeout(() => delete form.dataset.enviando, 4000);
    });

    async function copiarTexto(boton) {
        const origen = document.querySelector(boton.dataset.copy);
        const texto = origen ? origen.textContent.trim() : '';
        const etiqueta = boton.querySelector('[data-copy-label]') || boton;
        const original = etiqueta.textContent;
        let ok = false;
        try {
            await navigator.clipboard.writeText(texto);
            ok = true;
        } catch {
            // Alternativa para navegadores sin Clipboard API.
            const area = document.createElement('textarea');
            area.value = texto;
            area.setAttribute('readonly', '');
            area.style.position = 'fixed';
            area.style.opacity = '0';
            document.body.appendChild(area);
            area.select();
            try { ok = document.execCommand('copy'); } catch { ok = false; }
            area.remove();
        }
        etiqueta.textContent = ok ? '✅ ¡Copiado!' : 'Mantén presionado el código para copiarlo';
        setTimeout(() => { etiqueta.textContent = original; }, 2500);
    }

    function abrirRegalo(regalo) {
        if (regalo.classList.contains('abierto')) return;
        regalo.classList.add('abierto');
        regalo.setAttribute('aria-expanded', 'true');
        const escena = regalo.closest('[data-gift-scene]');
        const revelacion = document.getElementById(regalo.getAttribute('aria-controls'));

        setTimeout(() => {
            if (escena) escena.classList.add('oculto');
            if (revelacion) {
                revelacion.hidden = false;
                revelacion.focus({ preventScroll: true });
                revelacion.scrollIntoView({ behavior: sinMovimiento ? 'auto' : 'smooth', block: 'center' });
            }
            if (!sinMovimiento) lanzarConfeti();
        }, sinMovimiento ? 0 : 750);
    }

    function lanzarConfeti() {
        const lienzo = document.createElement('canvas');
        lienzo.id = 'confeti';
        lienzo.setAttribute('aria-hidden', 'true');
        document.body.appendChild(lienzo);
        const ctx = lienzo.getContext('2d');
        const escala = window.devicePixelRatio || 1;
        const ajustar = () => {
            lienzo.width = innerWidth * escala;
            lienzo.height = innerHeight * escala;
            ctx.setTransform(escala, 0, 0, escala, 0, 0);
        };
        ajustar();
        addEventListener('resize', ajustar);

        const colores = ['#9f1d2b', '#e8b54a', '#1d5c3f', '#ffffff', '#d43446', '#6fbf8e'];
        const piezas = Array.from({ length: 180 }, (_, i) => ({
            x: innerWidth / 2 + (Math.random() - .5) * 80,
            y: innerHeight * .45,
            vx: (Math.random() - .5) * 16,
            vy: -Math.random() * 16 - 6,
            ancho: 6 + Math.random() * 6,
            alto: 8 + Math.random() * 8,
            giro: Math.random() * Math.PI,
            vgiro: (Math.random() - .5) * .3,
            color: colores[i % colores.length],
        }));

        const inicio = performance.now();
        const DURACION = 4500;
        (function cuadro(ahora) {
            const t = ahora - inicio;
            ctx.clearRect(0, 0, innerWidth, innerHeight);
            ctx.globalAlpha = t > DURACION - 1000 ? Math.max(0, (DURACION - t) / 1000) : 1;
            for (const p of piezas) {
                p.vy += .35;           // gravedad
                p.vx *= .99;           // resistencia del aire
                p.x += p.vx;
                p.y += p.vy;
                p.giro += p.vgiro;
                ctx.save();
                ctx.translate(p.x, p.y);
                ctx.rotate(p.giro);
                ctx.fillStyle = p.color;
                ctx.fillRect(-p.ancho / 2, -p.alto / 2, p.ancho, p.alto * Math.cos(p.giro * 2));
                ctx.restore();
            }
            if (t < DURACION) requestAnimationFrame(cuadro);
            else { removeEventListener('resize', ajustar); lienzo.remove(); }
        })(inicio);
    }
})();
