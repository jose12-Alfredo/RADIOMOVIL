/// Modelos de los contratos `/api/v1/conductor/*` (ver docs/CONTRATOS.md).
/// La app no calcula estados: los recibe del servidor.
library;

enum EstadoConductor {
  disponible('Disponible'),
  ocupado('Ocupado'),
  fueraDeServicio('Fuera de servicio');

  const EstadoConductor(this.etiqueta);
  final String etiqueta;

  static EstadoConductor parse(Object? value) => switch (value) {
        'Disponible' => disponible,
        'Ocupado' => ocupado,
        _ => fueraDeServicio,
      };
}

/// Clasificación del GPS: Reciente ≤30 s, Desactualizado 31–90 s, SinSenal >90 s.
enum EstadoGps {
  reciente('GPS reciente'),
  desactualizado('GPS desactualizado'),
  sinSenal('Sin ubicación');

  const EstadoGps(this.etiqueta);
  final String etiqueta;

  static EstadoGps parse(Object? value) => switch (value) {
        'Reciente' => reciente,
        'Desactualizado' => desactualizado,
        _ => sinSenal,
      };

  static EstadoGps clasificar(Duration? edad, {int recienteSeg = 30, int desactualizadoSeg = 90}) {
    if (edad == null) return sinSenal;
    if (edad.inSeconds <= recienteSeg) return reciente;
    if (edad.inSeconds <= desactualizadoSeg) return desactualizado;
    return sinSenal;
  }
}

class TurnoActivo {
  const TurnoActivo({required this.id, required this.vehiculoId, required this.patente, required this.inicio});
  final String id;
  final String vehiculoId;
  final String patente;
  final DateTime inicio;

  factory TurnoActivo.fromJson(Map<String, dynamic> json) => TurnoActivo(
        id: json['id'] as String,
        vehiculoId: json['vehiculoId'] as String,
        patente: json['patente'] as String,
        inicio: DateTime.parse(json['inicio'] as String),
      );
}

class ConductorPropio {
  const ConductorPropio({
    required this.conductorId,
    required this.nombre,
    required this.estado,
    required this.ocupacion,
    required this.turno,
    required this.estadoGps,
    required this.ultimaRecepcion,
    required this.intervaloEnvioSegundos,
    this.carreraActivaId,
  });

  final String conductorId;
  final String nombre;
  final EstadoConductor estado;

  /// `Oferta`, `Viaje` o null.
  final String? ocupacion;
  final TurnoActivo? turno;
  final EstadoGps estadoGps;
  final DateTime? ultimaRecepcion;
  final int intervaloEnvioSegundos;

  /// Carrera asignada por la central (Asignado/EnCurso) o null.
  final String? carreraActivaId;

  bool get enTurno => turno != null;

  factory ConductorPropio.fromJson(Map<String, dynamic> json) {
    final turno = json['turno'];
    final ultima = json['ultimaRecepcion'];
    final intervalo = json['intervaloEnvioSegundos'];
    return ConductorPropio(
      conductorId: json['conductorId'] as String,
      nombre: json['nombre'] as String,
      estado: EstadoConductor.parse(json['estado']),
      ocupacion: json['ocupacion'] as String?,
      turno: turno is Map<String, dynamic> ? TurnoActivo.fromJson(turno) : null,
      estadoGps: EstadoGps.parse(json['estadoGps']),
      ultimaRecepcion: ultima is String ? DateTime.parse(ultima) : null,
      // El servidor exige ≥3 s; la app nunca envía más rápido de 5 s.
      intervaloEnvioSegundos: intervalo is num && intervalo >= 5 ? intervalo.toInt() : 5,
      carreraActivaId: json['carreraActivaId'] as String?,
    );
  }
}

class VehiculoAutorizado {
  const VehiculoAutorizado({required this.vehiculoId, required this.patente, required this.libre});
  final String vehiculoId;
  final String patente;
  final bool libre;

  factory VehiculoAutorizado.fromJson(Map<String, dynamic> json) => VehiculoAutorizado(
        vehiculoId: json['vehiculoId'] as String,
        patente: json['patente'] as String,
        libre: json['libre'] == true,
      );
}

class Sesion {
  const Sesion({required this.usuarioId, required this.email, required this.roles, required this.expiraEn});
  final String usuarioId;
  final String? email;
  final List<String> roles;
  final DateTime expiraEn;

  factory Sesion.fromJson(Map<String, dynamic> json) => Sesion(
        usuarioId: json['usuarioId'] as String,
        email: json['email'] as String?,
        roles: (json['roles'] as List? ?? const []).whereType<String>().toList(),
        expiraEn: DateTime.parse(json['expiraEn'] as String),
      );
}

class UbicacionCapturada {
  const UbicacionCapturada({
    required this.latitud,
    required this.longitud,
    required this.precisionMetros,
    required this.capturadaEn,
  });
  final double latitud;
  final double longitud;
  final double precisionMetros;
  final DateTime capturadaEn;

  Map<String, dynamic> toJson() => {
        'latitud': latitud,
        'longitud': longitud,
        'precisionMetros': precisionMetros,
        'capturadaEn': capturadaEn.toUtc().toIso8601String(),
      };
}

class ResultadoUbicacion {
  const ResultadoUbicacion({
    required this.aceptada,
    required this.motivo,
    required this.recibidaEn,
    required this.estadoGps,
    required this.intervaloMinimoSegundos,
  });
  final bool aceptada;

  /// `Repetida`, `MasAntiguaQueLaUltima` o null.
  final String? motivo;
  final DateTime recibidaEn;
  final EstadoGps estadoGps;
  final int intervaloMinimoSegundos;

  factory ResultadoUbicacion.fromJson(Map<String, dynamic> json) => ResultadoUbicacion(
        aceptada: json['aceptada'] == true,
        motivo: json['motivo'] as String?,
        recibidaEn: DateTime.parse(json['recibidaEn'] as String),
        estadoGps: EstadoGps.parse(json['estadoGps']),
        intervaloMinimoSegundos: (json['intervaloMinimoSegundos'] as num?)?.toInt() ?? 3,
      );
}

/// Punto WGS84 tal como lo marcó la operadora (sin precisión asociada).
class Punto {
  const Punto(this.latitud, this.longitud);
  final double latitud;
  final double longitud;

  static Punto? deJson(Object? lat, Object? lng) {
    if (lat is! num || lng is! num) return null;
    final la = lat.toDouble(), lo = lng.toDouble();
    if (la < -90 || la > 90 || lo < -180 || lo > 180) return null;
    return Punto(la, lo);
  }

  @override
  bool operator ==(Object other) => other is Punto && other.latitud == latitud && other.longitud == longitud;
  @override
  int get hashCode => Object.hash(latitud, longitud);
}

enum EstadoCarrera {
  asignado,
  enCurso;

  static EstadoCarrera parse(Object? value) => value == 'EnCurso' ? enCurso : asignado;
}

/// Carrera asignada directamente por la central (`GET conductor/carreras`).
/// El conductor no la acepta ni la rechaza.
class CarreraAsignada {
  const CarreraAsignada({
    required this.viajeId,
    required this.estado,
    required this.asignadoEn,
    required this.recibidaEn,
    required this.origen,
    required this.recogida,
    required this.destino,
    required this.destinoPunto,
    required this.observaciones,
    required this.nombrePasajero,
    required this.telefonoPasajero,
  });

  final String viajeId;
  final EstadoCarrera estado;
  final DateTime asignadoEn;
  final DateTime? recibidaEn;
  final String origen;
  final Punto? recogida;
  final String? destino;
  final Punto? destinoPunto;
  final String? observaciones;
  final String? nombrePasajero;
  final String? telefonoPasajero;

  /// A dónde debe ir ahora: recogida mientras está Asignado; destino en curso.
  Punto? get objetivo => estado == EstadoCarrera.asignado ? recogida : destinoPunto;

  static String? _texto(Object? v) => v is String && v.trim().isNotEmpty ? v.trim() : null;

  factory CarreraAsignada.fromJson(Map<String, dynamic> json) {
    final recibida = json['recibidaEn'];
    return CarreraAsignada(
      viajeId: json['viajeId'] as String,
      estado: EstadoCarrera.parse(json['estado']),
      asignadoEn: DateTime.parse(json['asignadoEn'] as String),
      recibidaEn: recibida is String ? DateTime.parse(recibida) : null,
      origen: _texto(json['origen']) ?? 'Sin dirección registrada',
      recogida: Punto.deJson(json['recogidaLatitud'], json['recogidaLongitud']),
      destino: _texto(json['destino']),
      destinoPunto: Punto.deJson(json['destinoLatitud'], json['destinoLongitud']),
      observaciones: _texto(json['observaciones']),
      nombrePasajero: _texto(json['nombrePasajero']),
      telefonoPasajero: _texto(json['telefonoPasajero']),
    );
  }

  CarreraAsignada recibida(DateTime en) => CarreraAsignada(
        viajeId: viajeId,
        estado: estado,
        asignadoEn: asignadoEn,
        recibidaEn: en,
        origen: origen,
        recogida: recogida,
        destino: destino,
        destinoPunto: destinoPunto,
        observaciones: observaciones,
        nombrePasajero: nombrePasajero,
        telefonoPasajero: telefonoPasajero,
      );
}
