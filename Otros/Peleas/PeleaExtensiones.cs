﻿using Bot_Dofus_Retro.Otros.Game.Personaje.Configuracion;
using Bot_Dofus_Retro.Otros.Mapas;
using Bot_Dofus_Retro.Otros.Mapas.Movimiento.Peleas;
using Bot_Dofus_Retro.Otros.Peleas.Enums;
using Bot_Dofus_Retro.Otros.Peleas.Peleadores;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;

/*
    Este archivo es parte del proyecto Bot Dofus Retro

    Bot Dofus Retro Copyright (C) 2020 - 2021 Alvaro Prendes — Todos los derechos reservados.
    Creado por Alvaro Prendes
    web: http://www.salesprendes.com
*/

namespace Bot_Dofus_Retro.Otros.Peleas
{
    public class PeleaExtensiones : IDisposable
    {
        private Cuenta cuenta;
        private ManejadorHechizos manejador_hechizos;
        private Pelea pelea;

        private int hechizo_index;
        private byte turno;
        private bool esperando_sequencia;
        private bool hechizo_lanzado;
        private bool final_turno;
        private bool disposed;

        public PeleaGlobal configuracion;
        private ObservableCollection<PeleaHechizos> hechizos => configuracion.hechizos;

        public PeleaExtensiones(Cuenta _cuenta)
        {
            cuenta = _cuenta;
            configuracion = new PeleaGlobal(cuenta);
            manejador_hechizos = new ManejadorHechizos(cuenta);
            pelea = cuenta.juego.pelea;

            set_Eventos();
        }

        private void set_Eventos()
        {
            pelea.pelea_creada += get_Pelea_Creada;
            pelea.turno_iniciado += get_Pelea_Turno_iniciado;
            hechizos.CollectionChanged += (sender, e) => get_Reordenar_Hechizos();
        }

        private void get_Pelea_Creada()
        {
            turno = 0;
            get_Reordenar_Hechizos();
        }

        private void get_Reordenar_Hechizos()
        {
            if (!cuenta.esta_Luchando())
                return;

            foreach(PeleaHechizos hechizo in hechizos)
            {
                hechizo.turno_lanzado = 0;
                hechizo.lanzamientos_restantes = hechizo.lanzamientos_x_turno;
            }
        }

        private async void get_Pelea_Turno_iniciado()
        {
            turno++;
            hechizo_index = 0;
            esperando_sequencia = true;
            hechizo_lanzado = false;
            final_turno = false;

            if (hechizos.Count == 0 || !pelea.get_Enemigos.Any())
            {
                await antes_Fin_Turno();
                return;
            }

            await get_Procesar_hechizo();
        }

        private async Task get_Procesar_hechizo()
        {
            if (cuenta?.esta_Luchando() == false || configuracion == null)
                return;

            if (hechizo_index >= hechizos.Count)
            {
                await antes_Fin_Turno();
                return;
            }

            PeleaHechizos hechizo_actual = hechizos[hechizo_index];

            if (hechizo_actual.lanzamientos_restantes == 0)
            {
                await get_Procesar_Siguiente_Hechizo(hechizo_actual, true);
                return;
            }

            if (!(turno == 1 || hechizo_actual.turno_lanzado == 0 || turno == (hechizo_actual.turno_lanzado + 1)))
            {
                await get_Procesar_Siguiente_Hechizo(hechizo_actual);
                return;
            }

            ResultadoLanzandoHechizo resultado = await manejador_hechizos.manejador_Hechizos(hechizo_actual);
            switch (resultado)
            {
                case ResultadoLanzandoHechizo.NO_LANZADO:
                    await get_Procesar_Siguiente_Hechizo(hechizo_actual);
                break;

                case ResultadoLanzandoHechizo.LANZADO:
                    hechizo_lanzado = true;
                    hechizo_actual.lanzamientos_restantes--;
                    esperando_sequencia = true;
                break;

                case ResultadoLanzandoHechizo.MOVIDO:
                    esperando_sequencia = true;
                break;
            }
        }

        private async Task get_Procesar_Siguiente_Hechizo(PeleaHechizos hechizo_actual, bool actualizar_turno = false)
        {
            if (cuenta?.esta_Luchando() == false)
                return;

            hechizo_actual.lanzamientos_restantes = hechizo_actual.lanzamientos_x_turno;
            hechizo_index++;

            if (actualizar_turno)
                hechizo_actual.turno_lanzado = turno;

            await get_Procesar_hechizo();
        }

        public async Task get_Procesar_Accion_Sin_Lanzar_Hechizo()
        {
            try
            {
                if (hechizo_index >= hechizos.Count)
                    await antes_Fin_Turno();
                else
                    await get_Procesar_Siguiente_Hechizo(hechizos[hechizo_index]);
            }
            catch {}
        }

        public async Task get_Procesar_Accion_Finalizada()
        {
            if (!pelea.turno_actual)
                return;

            if (pelea.total_enemigos_vivos == 0)
                return;

            if (final_turno)
                await fin_Turno(400);

            if (esperando_sequencia)
                esperando_sequencia = false;
            else
                return;

            await get_Procesar_hechizo();
        }

        private async Task antes_Fin_Turno()
        {
            if (!pelea.turno_actual)
                return;

            if (configuracion.tactica == Tactica.PASIVA)
            {
                await fin_Turno(400);
                return;
            }

            if (pelea.esta_Cuerpo_A_Cuerpo_Con_Enemigo() && configuracion.tactica == Tactica.AGRESIVA)
            {
                await fin_Turno(400);
                return;
            }

            if (pelea.jugador_luchador.pm > 0 && pelea.get_Enemigos.Any())
            {
                bool cercano = configuracion.tactica == Tactica.AGRESIVA || !hechizo_lanzado;
                await get_Mover(cercano, pelea.get_Enemigo_Mas_Cercano());
                final_turno = true;
            }

            await fin_Turno(400);
        }

        private async Task fin_Turno(int delay)
        {
            await Task.Delay(delay);
            await cuenta.conexion.enviar_Paquete_Async("Gt");
        }

        public async Task get_Mover(bool cercano, Luchadores enemigo)
        {
            KeyValuePair<short, MovimientoNodo>? nodo = null;
            Mapa mapa = cuenta.juego.mapa;
            int distancia = -1;

            int distancia_total = Get_Total_Distancia_Enemigo(pelea.jugador_luchador.celda);

            foreach (KeyValuePair<short, MovimientoNodo> kvp in PeleasPathfinder.get_Celdas_Accesibles(pelea, mapa, pelea.jugador_luchador.celda))
            {
                if (!kvp.Value.alcanzable)
                    continue;

                int temporal_distancia = Get_Total_Distancia_Enemigo(mapa.get_Celda_Id(kvp.Key));

                if ((cercano && temporal_distancia <= distancia_total) || (!cercano && temporal_distancia >= distancia_total))
                {
                    if (cercano)
                    {
                        nodo = kvp;
                        distancia_total = temporal_distancia;
                    }
                    else if (kvp.Value.camino.celdas_accesibles.Count >= distancia)
                    {
                        nodo = kvp;
                        distancia_total = temporal_distancia;
                        distancia = kvp.Value.camino.celdas_accesibles.Count;
                    }
                }
            }

            if (nodo != null)
                await cuenta.juego.manejador.movimientos.get_Mover_Celda_Pelea(nodo);
        }

        public int Get_Total_Distancia_Enemigo(Celda celda) => pelea.get_Enemigos.Sum(e => e.celda.get_Distancia(celda) - 1);

        #region Zona Dispose
        public void Dispose() => Dispose(true);
        ~PeleaExtensiones() => Dispose(false);

        public virtual void Dispose(bool disposing)
        {
            if (!disposed)
            {
                if (disposing)
                {
                    configuracion.Dispose();
                    manejador_hechizos.Dispose();
                }

                configuracion = null;
                manejador_hechizos = null;
                cuenta = null;
                disposed = true;
            }
        }
        #endregion
    }
}
