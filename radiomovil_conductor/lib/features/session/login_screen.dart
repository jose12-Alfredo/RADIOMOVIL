import 'package:flutter/material.dart';

import '../../app/widgets.dart';
import 'driver_controller.dart';

class LoginScreen extends StatefulWidget {
  const LoginScreen({super.key, required this.controller});
  final DriverController controller;

  @override
  State<LoginScreen> createState() => _LoginScreenState();
}

class _LoginScreenState extends State<LoginScreen> {
  final _form = GlobalKey<FormState>();
  final _email = TextEditingController();
  final _password = TextEditingController();
  bool _ocultar = true;

  @override
  void dispose() {
    _email.dispose();
    _password.dispose();
    super.dispose();
  }

  Future<void> _ingresar() async {
    if (!(_form.currentState?.validate() ?? false)) return;
    FocusScope.of(context).unfocus();
    await widget.controller.login(_email.text, _password.text);
  }

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final scheme = theme.colorScheme;
    return Scaffold(
      backgroundColor: scheme.surface,
      body: SafeArea(
        child: Center(
          child: SingleChildScrollView(
            padding: const EdgeInsets.fromLTRB(24, 32, 24, 24),
            child: ConstrainedBox(
              constraints: const BoxConstraints(maxWidth: 420),
              child: ListenableBuilder(
                listenable: widget.controller,
                builder: (context, _) {
                  final c = widget.controller;
                  return AutofillGroup(
                    child: Form(
                      key: _form,
                      child: Column(
                        crossAxisAlignment: CrossAxisAlignment.stretch,
                        children: [
                          const Align(alignment: Alignment.centerLeft, child: BrandMark(size: 56)),
                          const SizedBox(height: 32),
                          Text('Ingreso de conductores', style: theme.textTheme.headlineSmall),
                          const SizedBox(height: 8),
                          Text('Usa la cuenta que te entregó tu central.',
                              style: theme.textTheme.bodyLarge?.copyWith(color: scheme.onSurfaceVariant)),
                          const SizedBox(height: 32),
                          TextFormField(
                            controller: _email,
                            enabled: !c.operando,
                            keyboardType: TextInputType.emailAddress,
                            textInputAction: TextInputAction.next,
                            autofillHints: const [AutofillHints.username, AutofillHints.email],
                            autocorrect: false,
                            decoration: const InputDecoration(labelText: 'Correo'),
                            validator: (v) {
                              final value = v?.trim() ?? '';
                              if (value.isEmpty) return 'Escribe tu correo.';
                              if (!value.contains('@') || value.length > 256) return 'Revisa el formato del correo.';
                              return null;
                            },
                          ),
                          const SizedBox(height: 20),
                          TextFormField(
                            controller: _password,
                            enabled: !c.operando,
                            obscureText: _ocultar,
                            textInputAction: TextInputAction.done,
                            autofillHints: const [AutofillHints.password],
                            onFieldSubmitted: (_) => _ingresar(),
                            decoration: InputDecoration(
                              labelText: 'Contraseña',
                              suffixIcon: IconButton(
                                tooltip: _ocultar ? 'Mostrar contraseña' : 'Ocultar contraseña',
                                icon: Icon(_ocultar ? Icons.visibility_outlined : Icons.visibility_off_outlined),
                                onPressed: () => setState(() => _ocultar = !_ocultar),
                              ),
                            ),
                            validator: (v) => (v == null || v.isEmpty)
                                ? 'Escribe tu contraseña.'
                                : (v.length > 256 ? 'La contraseña es demasiado larga.' : null),
                          ),
                          if (c.error != null) ...[
                            const SizedBox(height: 20),
                            Notice(message: c.error!, tone: NoticeTone.error),
                          ],
                          const SizedBox(height: 28),
                          FilledButton(
                            onPressed: c.operando ? null : _ingresar,
                            child: c.operando
                                ? const SizedBox.square(dimension: 22, child: CircularProgressIndicator(strokeWidth: 2.5))
                                : const Text('Ingresar'),
                          ),
                        ],
                      ),
                    ),
                  );
                },
              ),
            ),
          ),
        ),
      ),
    );
  }
}
