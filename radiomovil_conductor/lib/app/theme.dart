import 'package:flutter/material.dart';

/// Sistema visual de la app de conductores: el mundo RadioMovil
/// (frontend-web/DESIGN.md: azul de acción, marino de marca, neutros fríos)
/// expresado mediante roles de Material 3. Ver DESIGN.md de este proyecto.
abstract final class RmColors {
  static const action = Color(0xFF184F9D);
  static const brand = Color(0xFF13355A);
  static const ink = Color(0xFF17212F);
  static const muted = Color(0xFF526071);
  static const canvas = Color(0xFFF4F6F8);
}

/// Colores de estado operativo. Siempre acompañados de icono y texto.
@immutable
class RmStatusColors extends ThemeExtension<RmStatusColors> {
  const RmStatusColors({
    required this.ok,
    required this.onOk,
    required this.okContainer,
    required this.onOkContainer,
    required this.warn,
    required this.warnContainer,
    required this.onWarnContainer,
    required this.offContainer,
    required this.onOffContainer,
  });

  final Color ok;
  final Color onOk;
  final Color okContainer;
  final Color onOkContainer;
  final Color warn;
  final Color warnContainer;
  final Color onWarnContainer;
  final Color offContainer;
  final Color onOffContainer;

  static const light = RmStatusColors(
    ok: Color(0xFF1E6B3A),
    onOk: Color(0xFFFFFFFF),
    okContainer: Color(0xFFDDF2E3),
    onOkContainer: Color(0xFF0E3B1E),
    warn: Color(0xFF8A5A00),
    warnContainer: Color(0xFFFFEFC7),
    onWarnContainer: Color(0xFF4A3000),
    offContainer: Color(0xFFE6EBF1),
    onOffContainer: Color(0xFF34485E),
  );

  static const dark = RmStatusColors(
    ok: Color(0xFF86D7A0),
    onOk: Color(0xFF0B2E17),
    okContainer: Color(0xFF14391F),
    onOkContainer: Color(0xFFC4F0D0),
    warn: Color(0xFFF3C063),
    warnContainer: Color(0xFF3D2C06),
    onWarnContainer: Color(0xFFFFE2A6),
    offContainer: Color(0xFF26313F),
    onOffContainer: Color(0xFFC9D3DF),
  );

  @override
  RmStatusColors copyWith() => this;

  @override
  RmStatusColors lerp(RmStatusColors? other, double t) => t < .5 || other == null ? this : other;
}

abstract final class RmTheme {
  static const _lightScheme = ColorScheme(
    brightness: Brightness.light,
    primary: RmColors.action,
    onPrimary: Color(0xFFFFFFFF),
    primaryContainer: Color(0xFFD8E4F8),
    onPrimaryContainer: Color(0xFF0E2F5F),
    secondary: RmColors.brand,
    onSecondary: Color(0xFFFFFFFF),
    secondaryContainer: Color(0xFFDCE6F2),
    onSecondaryContainer: Color(0xFF13355A),
    tertiary: Color(0xFF2971CB),
    onTertiary: Color(0xFFFFFFFF),
    error: Color(0xFFA52424),
    onError: Color(0xFFFFFFFF),
    errorContainer: Color(0xFFFFF0EF),
    onErrorContainer: Color(0xFF922222),
    surface: Color(0xFFFFFFFF),
    onSurface: RmColors.ink,
    onSurfaceVariant: RmColors.muted,
    surfaceContainerLowest: Color(0xFFFFFFFF),
    surfaceContainerLow: Color(0xFFF8F9FB),
    surfaceContainer: RmColors.canvas,
    surfaceContainerHigh: Color(0xFFEDF1F5),
    surfaceContainerHighest: Color(0xFFE6EBF1),
    outline: Color(0xFF8A98A9),
    outlineVariant: Color(0xFFE1E6EC),
    inverseSurface: Color(0xFF17212F),
    onInverseSurface: Color(0xFFF1F5FA),
    inversePrimary: Color(0xFFA9C7FF),
    shadow: Color(0xFF000000),
    scrim: Color(0xFF000000),
  );

  static const _darkScheme = ColorScheme(
    brightness: Brightness.dark,
    primary: Color(0xFFA9C7FF),
    onPrimary: Color(0xFF0B2F63),
    primaryContainer: Color(0xFF1F477F),
    onPrimaryContainer: Color(0xFFD8E4FF),
    secondary: Color(0xFFB7C8DE),
    onSecondary: Color(0xFF1D3247),
    secondaryContainer: Color(0xFF2B3E54),
    onSecondaryContainer: Color(0xFFD5E3F7),
    tertiary: Color(0xFF9CCAFF),
    onTertiary: Color(0xFF00325A),
    error: Color(0xFFFFB4AB),
    onError: Color(0xFF690005),
    errorContainer: Color(0xFF5C1515),
    onErrorContainer: Color(0xFFFFDAD6),
    surface: Color(0xFF0F1620),
    onSurface: Color(0xFFE3E8EF),
    onSurfaceVariant: Color(0xFFAAB6C4),
    surfaceContainerLowest: Color(0xFF0A1018),
    surfaceContainerLow: Color(0xFF151E2A),
    surfaceContainer: Color(0xFF1A2430),
    surfaceContainerHigh: Color(0xFF222D3A),
    surfaceContainerHighest: Color(0xFF2C3746),
    outline: Color(0xFF74818F),
    outlineVariant: Color(0xFF2C3746),
    inverseSurface: Color(0xFFE3E8EF),
    onInverseSurface: Color(0xFF17212F),
    inversePrimary: RmColors.action,
    shadow: Color(0xFF000000),
    scrim: Color(0xFF000000),
  );

  static ThemeData light() => _build(_lightScheme, RmStatusColors.light);
  static ThemeData dark() => _build(_darkScheme, RmStatusColors.dark);

  static ThemeData _build(ColorScheme scheme, RmStatusColors status) {
    final base = ThemeData(colorScheme: scheme, useMaterial3: true);
    final text = base.textTheme.copyWith(
      headlineSmall: base.textTheme.headlineSmall?.copyWith(fontWeight: FontWeight.w700, letterSpacing: -0.4),
      titleLarge: base.textTheme.titleLarge?.copyWith(fontWeight: FontWeight.w700, letterSpacing: -0.2),
      titleMedium: base.textTheme.titleMedium?.copyWith(fontWeight: FontWeight.w600),
      labelLarge: base.textTheme.labelLarge?.copyWith(fontWeight: FontWeight.w600, fontSize: 16),
    );
    const radius = BorderRadius.all(Radius.circular(12));
    const buttonSize = Size.fromHeight(56);
    return base.copyWith(
      textTheme: text,
      extensions: [status],
      scaffoldBackgroundColor: scheme.surfaceContainer,
      filledButtonTheme: FilledButtonThemeData(
        style: FilledButton.styleFrom(minimumSize: buttonSize, shape: const RoundedRectangleBorder(borderRadius: radius), textStyle: text.labelLarge),
      ),
      outlinedButtonTheme: OutlinedButtonThemeData(
        style: OutlinedButton.styleFrom(minimumSize: buttonSize, shape: const RoundedRectangleBorder(borderRadius: radius), textStyle: text.labelLarge),
      ),
      textButtonTheme: TextButtonThemeData(style: TextButton.styleFrom(minimumSize: const Size(48, 48))),
      inputDecorationTheme: InputDecorationTheme(
        filled: true,
        fillColor: scheme.surface,
        border: OutlineInputBorder(borderRadius: radius, borderSide: BorderSide(color: scheme.outline)),
        enabledBorder: OutlineInputBorder(borderRadius: radius, borderSide: BorderSide(color: scheme.outline)),
        focusedBorder: OutlineInputBorder(borderRadius: radius, borderSide: BorderSide(color: scheme.tertiary, width: 2)),
        contentPadding: const EdgeInsets.symmetric(horizontal: 16, vertical: 16),
      ),
      snackBarTheme: const SnackBarThemeData(behavior: SnackBarBehavior.floating),
      bottomSheetTheme: BottomSheetThemeData(
        backgroundColor: scheme.surface,
        showDragHandle: true,
        shape: const RoundedRectangleBorder(borderRadius: BorderRadius.vertical(top: Radius.circular(24))),
      ),
    );
  }
}

extension RmThemeX on BuildContext {
  RmStatusColors get status => Theme.of(this).extension<RmStatusColors>()!;
}
