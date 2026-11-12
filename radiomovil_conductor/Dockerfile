# Compilación reproducible del APK (opcional; NO es necesaria para desarrollar).
# Imagen con Flutter + Android SDK fijados a la misma versión que el entorno local.
#
#   docker build -t radiomovil-conductor-build \
#     --build-arg RM_API_BASE_URL=https://<ip-o-dominio>:<puerto> \
#     --build-arg RM_EMPRESA_ID=<guid> .
#   docker create --name rmc radiomovil-conductor-build
#   docker cp rmc:/out/app-release.apk ./build/
#   docker rm rmc
#
# Qué compila: análisis, pruebas unitarias/widget y APK release (firmado con clave debug
# hasta configurar la firma real). Qué NO prueba: GPS, permisos, certificados en el
# teléfono, mapa ni red real: eso exige un teléfono Android.
# Requisito: maplibre_gl compila con Java 21; si la imagen trae otro JDK, la compilación
# fallará con "invalid source release: 21" (verificar al ejecutarla por primera vez).
FROM ghcr.io/cirruslabs/flutter:3.47.9

ARG RM_API_BASE_URL
ARG RM_EMPRESA_ID

WORKDIR /src
COPY pubspec.yaml pubspec.lock ./
RUN flutter pub get
COPY . .
RUN flutter analyze && flutter test
RUN test -n "$RM_API_BASE_URL" && test -n "$RM_EMPRESA_ID" \
 && flutter build apk --release \
      --dart-define=RM_API_BASE_URL=$RM_API_BASE_URL \
      --dart-define=RM_EMPRESA_ID=$RM_EMPRESA_ID \
 && mkdir -p /out && cp build/app/outputs/flutter-apk/app-release.apk /out/
