using RadioMovil.Domain;
using Xunit;

namespace RadioMovil.UnitTests;

public sealed class DominioTests
{
    private static readonly Guid Empresa = Guid.NewGuid();
    private static readonly Guid Operador = Guid.NewGuid();
    private static readonly Guid Chofer = Guid.NewGuid();
    private static readonly DateTimeOffset Ahora = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);
    private static Solicitud Nueva() => new(Empresa, "Terminal", Ahora, Operador);
    private static (Solicitud solicitud, IntentoDespacho intento) Ofertada()
    {
        var solicitud = Nueva();
        return (solicitud, solicitud.Ofrecer(Chofer, Ahora, Ahora.AddSeconds(20), Operador));
    }

    [Fact] public void CrearSolicitud_RegistraIdentidadYEvento()
    {
        var solicitud = Nueva();
        Assert.NotEqual(Guid.Empty, solicitud.Id);
        Assert.Equal(EstadoSolicitud.Pendiente, solicitud.Estado);
        Assert.Single(solicitud.Eventos);
        Assert.Throws<ErrorDominio>(() => new Solicitud(Guid.Empty, "Terminal", Ahora, Operador));
    }

    [Fact] public void Transiciones_InvalidasYOfertaDuplicada_SeRechazan()
    {
        var (solicitud, _) = Ofertada();
        Assert.Throws<ErrorDominio>(() => solicitud.Ofrecer(Guid.NewGuid(), Ahora, Ahora.AddSeconds(20), Operador));
        Assert.Throws<ErrorDominio>(() => solicitud.FinalizarViaje(Guid.NewGuid(), Ahora, Operador));
    }

    [Fact] public void Rechazar_ConservaSolicitudEHistorialYPermiteReofertar()
    {
        var (solicitud, intento) = Ofertada();
        solicitud.Rechazar(intento.Id, Ahora.AddSeconds(1), Chofer, "No disponible");
        Assert.Equal(EstadoIntento.Rechazado, intento.Estado);
        Assert.Equal(EstadoSolicitud.Pendiente, solicitud.Estado);
        solicitud.Ofrecer(Guid.NewGuid(), Ahora.AddSeconds(2), Ahora.AddSeconds(22), Operador);
        Assert.Equal(2, solicitud.Intentos.Count);
        Assert.Equal(4, solicitud.Eventos.Count);
    }

    [Fact] public void Vencimiento_NoPermiteAceptacionTardia()
    {
        var (solicitud, intento) = Ofertada();
        Assert.Throws<ErrorDominio>(() => solicitud.Aceptar(intento.Id, intento.VenceEn, Chofer));
        solicitud.Vencer(intento.Id, intento.VenceEn, Operador);
        Assert.Equal(EstadoIntento.Vencido, intento.Estado);
        Assert.Throws<ErrorDominio>(() => solicitud.Aceptar(intento.Id, intento.VenceEn, Chofer));
    }

    [Fact] public void CancelarCliente_CierraOfertaYConservaHistoria()
    {
        var (solicitud, intento) = Ofertada();
        solicitud.CancelarCliente(Ahora.AddSeconds(1), Operador, "Cliente desiste");
        Assert.Equal(EstadoSolicitud.Cancelada, solicitud.Estado);
        Assert.Equal(EstadoIntento.Cancelado, intento.Estado);
        Assert.Equal(3, solicitud.Eventos.Count);
        Assert.Throws<ErrorDominio>(() => solicitud.Ofrecer(Chofer, Ahora.AddSeconds(2), Ahora.AddSeconds(22), Operador));
    }

    [Fact] public void CancelarConductorAntesDeRecoger_ConservaViajeAnteriorYVuelveAPendiente()
    {
        var (solicitud, intento) = Ofertada();
        var viaje = solicitud.Aceptar(intento.Id, Ahora.AddSeconds(1), Chofer);
        solicitud.CancelarConductorAntesDeRecoger(viaje, Ahora.AddSeconds(2), Chofer, "Avería");
        Assert.Equal(EstadoSolicitud.Pendiente, solicitud.Estado);
        Assert.Null(solicitud.ViajeId);
        Assert.Equal(EstadoViaje.Incidencia, viaje.Estado);
        Assert.Contains(solicitud.Eventos, e => e.Tipo == "CanceladaConductor");
    }

    [Fact] public void CancelacionAsignada_ExigeCerrarViajeEnLaMismaOperacion()
    {
        var (solicitud, intento) = Ofertada();
        var viaje = solicitud.Aceptar(intento.Id, Ahora.AddSeconds(1), Operador);
        Assert.Throws<ErrorDominio>(() => solicitud.CancelarCliente(Ahora.AddSeconds(2), Operador, "Desiste"));
        Assert.Equal(EstadoSolicitud.Asignada, solicitud.Estado);
        solicitud.CancelarCliente(viaje, Ahora.AddSeconds(2), Operador, "Desiste");
        Assert.Equal(EstadoSolicitud.Cancelada, solicitud.Estado);
        Assert.Null(solicitud.ViajeId);
        Assert.Equal(EstadoViaje.Incidencia, viaje.Estado);
        Assert.Throws<ErrorDominio>(() => solicitud.Ofrecer(Chofer, Ahora.AddSeconds(3), Ahora.AddSeconds(23), Operador));
    }

    [Fact] public void Finalizada_NoSeReabre()
    {
        var (solicitud, intento) = Ofertada();
        var viaje = solicitud.Aceptar(intento.Id, Ahora.AddSeconds(1), Chofer);
        viaje.IrARecoger(Ahora.AddSeconds(1));
        viaje.IniciarRecorrido(Ahora.AddSeconds(2));
        solicitud.IniciarViaje(viaje.Id, Ahora.AddSeconds(2), Chofer);
        viaje.Finalizar(Ahora.AddSeconds(3));
        solicitud.FinalizarViaje(viaje.Id, Ahora.AddSeconds(3), Chofer);
        Assert.Throws<ErrorDominio>(() => solicitud.CancelarCliente(Ahora.AddSeconds(4), Operador, "Tarde"));
        Assert.Throws<ErrorDominio>(() => viaje.IniciarRecorrido(Ahora.AddSeconds(4)));
        Assert.Equal(5, solicitud.Eventos.Count);
    }
}
