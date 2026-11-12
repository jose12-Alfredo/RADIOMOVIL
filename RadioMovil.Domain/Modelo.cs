namespace RadioMovil.Domain;

public sealed class ErrorDominio(string message) : InvalidOperationException(message);

public static class Identidad
{
    public static Guid Exigir(Guid id, string nombre) => id != Guid.Empty ? id : throw new ErrorDominio($"{nombre} es obligatorio.");
    public static string Texto(string? valor, string nombre) => !string.IsNullOrWhiteSpace(valor) ? valor.Trim() : throw new ErrorDominio($"{nombre} es obligatorio.");
}

public enum EstadoSolicitud { Pendiente, Ofertada, Asignada, EnViaje, Finalizada, Cancelada }
public enum EstadoIntento { Ofertado, Aceptado, Rechazado, Vencido, Cancelado }
// Se guarda como TEXTO: el orden de los valores no afecta a los datos existentes.
public enum EstadoViaje { Asignado, EnCurso, Finalizado, Incidencia, EnCamino, EnPuerta }

public sealed record SolicitudEvento(Guid Id, Guid SolicitudId, DateTimeOffset Fecha, Guid ActorId, string Tipo, string Motivo);

public sealed class IntentoDespacho
{
    public Guid Id { get; private set; }
    public Guid EmpresaId { get; private set; }
    public Guid SolicitudId { get; private set; }
    public Guid ConductorId { get; private set; }
    public DateTimeOffset CreadoEn { get; private set; }
    public DateTimeOffset VenceEn { get; private set; }
    public EstadoIntento Estado { get; private set; } = EstadoIntento.Ofertado;
    private IntentoDespacho() { }

    internal IntentoDespacho(Guid empresaId, Guid solicitudId, Guid conductorId, DateTimeOffset creadoEn, DateTimeOffset venceEn)
    {
        Id = Guid.NewGuid();
        EmpresaId = Identidad.Exigir(empresaId, nameof(empresaId));
        SolicitudId = Identidad.Exigir(solicitudId, nameof(solicitudId));
        ConductorId = Identidad.Exigir(conductorId, nameof(conductorId));
        if (venceEn <= creadoEn) throw new ErrorDominio("El vencimiento debe ser posterior a la oferta.");
        CreadoEn = creadoEn;
        VenceEn = venceEn;
    }

    internal void Resolver(EstadoIntento destino, DateTimeOffset fecha)
    {
        if (Estado != EstadoIntento.Ofertado) throw new ErrorDominio("La oferta ya fue resuelta.");
        if (fecha < CreadoEn) throw new ErrorDominio("La respuesta precede a la oferta.");
        if (destino == EstadoIntento.Aceptado && fecha >= VenceEn) throw new ErrorDominio("No se puede aceptar una oferta vencida.");
        if (destino == EstadoIntento.Vencido && fecha < VenceEn) throw new ErrorDominio("La oferta todavía no ha vencido.");
        if (destino is not (EstadoIntento.Aceptado or EstadoIntento.Rechazado or EstadoIntento.Vencido or EstadoIntento.Cancelado))
            throw new ErrorDominio("Resultado de oferta inválido.");
        Estado = destino;
    }
}

public sealed class Solicitud
{
    private readonly List<IntentoDespacho> intentos = [];
    private readonly List<SolicitudEvento> eventos = [];
    public Guid Id { get; private set; }
    public Guid EmpresaId { get; private set; }
    public string Origen { get; private set; } = null!;
    public DateTimeOffset CreadaEn { get; private set; }
    public EstadoSolicitud Estado { get; private set; } = EstadoSolicitud.Pendiente;
    public Guid? ViajeId { get; private set; }
    public IReadOnlyList<IntentoDespacho> Intentos => intentos.AsReadOnly();
    public IReadOnlyList<SolicitudEvento> Eventos => eventos.AsReadOnly();
    private Solicitud() { }

    public Solicitud(Guid empresaId, string origen, DateTimeOffset fecha, Guid actorId)
    {
        Id = Guid.NewGuid();
        EmpresaId = Identidad.Exigir(empresaId, nameof(empresaId));
        Origen = Identidad.Texto(origen, nameof(origen));
        CreadaEn = fecha;
        Registrar(fecha, actorId, "Creada", "Solicitud registrada");
    }

    private void Registrar(DateTimeOffset fecha, Guid actorId, string tipo, string motivo)
    {
        Identidad.Exigir(actorId, nameof(actorId));
        if (fecha < CreadaEn) throw new ErrorDominio("Fecha anterior a la solicitud.");
        eventos.Add(new SolicitudEvento(Guid.NewGuid(), Id, fecha, actorId, tipo, Identidad.Texto(motivo, nameof(motivo))));
    }

    private void ExigirEstado(params EstadoSolicitud[] permitidos)
    {
        if (!permitidos.Contains(Estado)) throw new ErrorDominio($"Operación no permitida en estado {Estado}.");
    }

    public IntentoDespacho Ofrecer(Guid conductorId, DateTimeOffset fecha, DateTimeOffset venceEn, Guid actorId)
    {
        ExigirEstado(EstadoSolicitud.Pendiente);
        var intento = new IntentoDespacho(EmpresaId, Id, conductorId, fecha, venceEn);
        Registrar(fecha, actorId, "OfertaCreada", intento.Id.ToString());
        intentos.Add(intento);
        Estado = EstadoSolicitud.Ofertada;
        return intento;
    }

    private IntentoDespacho OfertaActiva(Guid intentoId)
    {
        ExigirEstado(EstadoSolicitud.Ofertada);
        var intento = intentos.SingleOrDefault(x => x.Id == intentoId && x.Estado == EstadoIntento.Ofertado);
        return intento ?? throw new ErrorDominio("La oferta no está activa para esta solicitud.");
    }

    public void Rechazar(Guid intentoId, DateTimeOffset fecha, Guid actorId, string motivo)
    {
        var intento = OfertaActiva(intentoId);
        Registrar(fecha, actorId, "OfertaRechazada", motivo);
        intento.Resolver(EstadoIntento.Rechazado, fecha);
        Estado = EstadoSolicitud.Pendiente;
    }

    public void Vencer(Guid intentoId, DateTimeOffset fecha, Guid actorId)
    {
        var intento = OfertaActiva(intentoId);
        if (fecha < intento.VenceEn) throw new ErrorDominio("La oferta todavía no ha vencido.");
        Registrar(fecha, actorId, "OfertaVencida", intentoId.ToString());
        intento.Resolver(EstadoIntento.Vencido, fecha);
        Estado = EstadoSolicitud.Pendiente;
    }

    public Viaje Aceptar(Guid intentoId, DateTimeOffset fecha, Guid actorId)
    {
        var intento = OfertaActiva(intentoId);
        if (fecha >= intento.VenceEn) throw new ErrorDominio("No se puede aceptar una oferta vencida.");
        Registrar(fecha, actorId, "OfertaAceptada", intentoId.ToString());
        intento.Resolver(EstadoIntento.Aceptado, fecha);
        var viaje = new Viaje(EmpresaId, Id, intento.ConductorId, fecha);
        ViajeId = viaje.Id;
        Estado = EstadoSolicitud.Asignada;
        return viaje;
    }

    /// <summary>
    /// Asignación DIRECTA por la central (decisión 2026-11-10): crea el viaje sin oferta ni aceptación del conductor.
    /// No se fabrica ningún IntentoDespacho; el historial registra quién asignó y a qué conductor.
    /// </summary>
    public Viaje AsignarDirecto(Guid conductorId, DateTimeOffset fecha, Guid actorId)
    {
        ExigirEstado(EstadoSolicitud.Pendiente);
        Identidad.Exigir(conductorId, nameof(conductorId));
        var viaje = new Viaje(EmpresaId, Id, conductorId, fecha);
        Registrar(fecha, actorId, "AsignadaDirecta", conductorId.ToString());
        ViajeId = viaje.Id;
        Estado = EstadoSolicitud.Asignada;
        return viaje;
    }

    public void CancelarCliente(DateTimeOffset fecha, Guid actorId, string motivo) => CancelarCliente(null, fecha, actorId, motivo);

    public void CancelarCliente(Viaje? viaje, DateTimeOffset fecha, Guid actorId, string motivo)
    {
        ExigirEstado(EstadoSolicitud.Pendiente, EstadoSolicitud.Ofertada, EstadoSolicitud.Asignada);
        Identidad.Exigir(actorId, nameof(actorId));
        Identidad.Texto(motivo, nameof(motivo));
        if (fecha < CreadaEn) throw new ErrorDominio("Fecha anterior a la solicitud.");
        if (Estado == EstadoSolicitud.Asignada)
        {
            if (viaje is null || ViajeId != viaje.Id || viaje.SolicitudId != Id || viaje.EmpresaId != EmpresaId || !Viaje.AntesDeRecoger.Contains(viaje.Estado))
                throw new ErrorDominio("La asignación debe cerrarse junto con la solicitud.");
            viaje.RegistrarIncidencia();
            ViajeId = null;
        }
        if (Estado == EstadoSolicitud.Ofertada)
        {
            var intento = intentos.Single(x => x.Estado == EstadoIntento.Ofertado);
            if (fecha < intento.CreadoEn) throw new ErrorDominio("La cancelación precede a la oferta.");
            Registrar(fecha, actorId, "CanceladaCliente", motivo);
            intento.Resolver(EstadoIntento.Cancelado, fecha);
        }
        else Registrar(fecha, actorId, "CanceladaCliente", motivo);
        Estado = EstadoSolicitud.Cancelada;
    }

    public void CancelarConductorAntesDeRecoger(Viaje viaje, DateTimeOffset fecha, Guid actorId, string motivo)
    {
        ExigirEstado(EstadoSolicitud.Asignada);
        ArgumentNullException.ThrowIfNull(viaje);
        if (ViajeId != viaje.Id || viaje.SolicitudId != Id || viaje.EmpresaId != EmpresaId || !Viaje.AntesDeRecoger.Contains(viaje.Estado))
            throw new ErrorDominio("El viaje no corresponde a una asignación previa a la recogida.");
        Registrar(fecha, actorId, "CanceladaConductor", motivo);
        viaje.RegistrarIncidencia();
        ViajeId = null;
        Estado = EstadoSolicitud.Pendiente;
    }

    public void IniciarViaje(Guid viajeId, DateTimeOffset fecha, Guid actorId)
    {
        ExigirEstado(EstadoSolicitud.Asignada);
        if (ViajeId != viajeId) throw new ErrorDominio("Viaje ajeno a la solicitud.");
        Registrar(fecha, actorId, "ViajeIniciado", viajeId.ToString());
        Estado = EstadoSolicitud.EnViaje;
    }

    public void FinalizarViaje(Guid viajeId, DateTimeOffset fecha, Guid actorId)
    {
        ExigirEstado(EstadoSolicitud.EnViaje);
        if (ViajeId != viajeId) throw new ErrorDominio("Viaje ajeno a la solicitud.");
        Registrar(fecha, actorId, "ViajeFinalizado", viajeId.ToString());
        Estado = EstadoSolicitud.Finalizada;
    }
}

public sealed class Viaje
{
    public Guid Id { get; private set; } = Guid.NewGuid();
    public Guid EmpresaId { get; private set; }
    public Guid SolicitudId { get; private set; }
    public Guid ConductorId { get; private set; }
    public DateTimeOffset AsignadoEn { get; private set; }
    public EstadoViaje Estado { get; private set; } = EstadoViaje.Asignado;
    /// <summary>Primera vez que la app del conductor confirmó ver la orden. Es entrega, NO aceptación: no cambia el estado.</summary>
    public DateTimeOffset? RecibidaEn { get; private set; }
    private Viaje() { }
    /// <returns>true si esta llamada registró la recepción; false si ya constaba (idempotente).</returns>
    public bool ConfirmarRecepcion(DateTimeOffset fecha)
    {
        if (!EstaActivo) throw new ErrorDominio("El viaje está cerrado.");
        if (RecibidaEn is not null) return false;
        if (fecha < AsignadoEn) throw new ErrorDominio("La recepción precede a la asignación.");
        RecibidaEn = fecha;
        return true;
    }
    public Viaje(Guid empresaId, Guid solicitudId, Guid conductorId, DateTimeOffset asignadoEn)
    {
        EmpresaId = Identidad.Exigir(empresaId, nameof(empresaId));
        SolicitudId = Identidad.Exigir(solicitudId, nameof(solicitudId));
        ConductorId = Identidad.Exigir(conductorId, nameof(conductorId));
        AsignadoEn = asignadoEn;
    }
    /// <summary>Estados que ocupan al conductor y a la solicitud (índices únicos parciales y trigger usan la MISMA lista).</summary>
    public static readonly EstadoViaje[] Activos = [EstadoViaje.Asignado, EstadoViaje.EnCamino, EstadoViaje.EnPuerta, EstadoViaje.EnCurso];
    /// <summary>Antes de recoger al pasajero: la central aún puede cancelar o reasignar.</summary>
    public static readonly EstadoViaje[] AntesDeRecoger = [EstadoViaje.Asignado, EstadoViaje.EnCamino, EstadoViaje.EnPuerta];
    public bool EstaActivo => Activos.Contains(Estado);

    private void Pasar(EstadoViaje destino, DateTimeOffset fecha, string error, params EstadoViaje[] desde)
    {
        if (!desde.Contains(Estado)) throw new ErrorDominio(EstaActivo ? error : "La carrera ya está cerrada.");
        if (fecha < AsignadoEn) throw new ErrorDominio("El paso precede a la asignación.");
        Estado = destino;
    }
    /// <summary>«Ir a recoger»: el conductor sale hacia la recogida.</summary>
    public void IrARecoger(DateTimeOffset fecha) => Pasar(EstadoViaje.EnCamino, fecha, "Solo se puede salir a recoger una carrera recién asignada.", EstadoViaje.Asignado);
    /// <summary>«Llegué»: está en la puerta del pasajero.</summary>
    public void MarcarLlegada(DateTimeOffset fecha) => Pasar(EstadoViaje.EnPuerta, fecha, "Primero indica que vas a recoger al pasajero.", EstadoViaje.EnCamino);
    /// <summary>«Iniciar recorrido»: pasajero a bordo. Se admite sin «Llegué».</summary>
    public void IniciarRecorrido(DateTimeOffset fecha) => Pasar(EstadoViaje.EnCurso, fecha, "Primero indica que vas a recoger al pasajero.", EstadoViaje.EnCamino, EstadoViaje.EnPuerta);
    public void Finalizar(DateTimeOffset fecha) => Pasar(EstadoViaje.Finalizado, fecha, "Solo se puede finalizar una carrera con el pasajero a bordo.", EstadoViaje.EnCurso);
    public void RegistrarIncidencia() { if (!EstaActivo) throw new ErrorDominio("El viaje está cerrado."); Estado = EstadoViaje.Incidencia; }
}

public sealed class Conductor
{
    public Guid Id { get; private set; } = Guid.NewGuid();
    public Guid EmpresaId { get; private set; }
    public string Nombre { get; private set; } = null!;
    private Conductor() { }
    public Conductor(Guid empresaId, string nombre)
    {
        EmpresaId = Identidad.Exigir(empresaId, nameof(empresaId));
        Nombre = Identidad.Texto(nombre, nameof(nombre));
        if (Nombre.Length > 160) throw new ErrorDominio("El nombre admite hasta 160 caracteres.");
    }
}

public sealed class Vehiculo
{
    public Guid Id { get; private set; } = Guid.NewGuid();
    public Guid EmpresaId { get; private set; }
    public string Patente { get; private set; } = null!;
    private Vehiculo() { }
    public Vehiculo(Guid empresaId, string patente) { EmpresaId = Identidad.Exigir(empresaId, nameof(empresaId)); Patente = NormalizarPatente(patente); }

    /// <summary>Una placa se compara sin espacios y en mayúsculas: "abc 123" y "ABC123" son la misma.</summary>
    public static string NormalizarPatente(string? patente)
    {
        var valor = string.Concat(Identidad.Texto(patente, nameof(patente)).Where(c => !char.IsWhiteSpace(c))).ToUpperInvariant();
        if (valor.Length is < 2 or > 16 || !valor.All(c => c is >= 'A' and <= 'Z' or >= '0' and <= '9' or '-'))
            throw new ErrorDominio("La placa admite de 2 a 16 letras, números o guiones.");
        return valor;
    }
}

public sealed class Turno
{
    public Guid Id { get; private set; } = Guid.NewGuid();
    public Guid EmpresaId { get; private set; }
    public Guid ConductorId { get; private set; }
    public Guid VehiculoId { get; private set; }
    public DateTimeOffset Inicio { get; private set; }
    public DateTimeOffset? Fin { get; private set; }
    // Nulos solo en turnos anteriores a la Fase B, creados sin actor registrado.
    public Guid? AbiertoPor { get; private set; }
    public Guid? CerradoPor { get; private set; }
    public string? MotivoCierre { get; private set; }
    private Turno() { }
    public Turno(Guid empresaId, Guid conductorId, Guid vehiculoId, DateTimeOffset inicio, Guid? abiertoPor = null)
    {
        EmpresaId = Identidad.Exigir(empresaId, nameof(empresaId));
        ConductorId = Identidad.Exigir(conductorId, nameof(conductorId));
        VehiculoId = Identidad.Exigir(vehiculoId, nameof(vehiculoId));
        Inicio = inicio;
        if (abiertoPor is { } actor) AbiertoPor = Identidad.Exigir(actor, nameof(abiertoPor));
    }
    public void Cerrar(DateTimeOffset fecha, Guid? actorId = null, string? motivo = null)
    {
        if (Fin is not null || fecha < Inicio) throw new ErrorDominio("No se puede cerrar el turno.");
        if (motivo is { Length: > 500 }) throw new ErrorDominio("El motivo admite hasta 500 caracteres.");
        if (actorId is { } actor) CerradoPor = Identidad.Exigir(actor, nameof(actorId));
        MotivoCierre = string.IsNullOrWhiteSpace(motivo) ? null : motivo.Trim();
        Fin = fecha;
    }
}
