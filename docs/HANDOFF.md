# Handoff — next task and backlog

The working context is [`AGENTS.md`](../AGENTS.md); the architecture is
[`TECHNICAL-DESIGN.md`](TECHNICAL-DESIGN.md). This file is only the next task's prompt and the
decisions already taken for what comes after.

## Next: scheduled summaries

Paste this to start the session:

```text
Trabajamos en `/home/santidev21/Dev/MyBudget-bot`, un bot de presupuesto personal para
Telegram (.NET 8 + PostgreSQL, Clean Architecture, modular monolith).

Antes de tocar nada:
1. Lee `AGENTS.md` completo y `docs/TECHNICAL-DESIGN.md` (§11 UX flows, §12 Phase 9,
   §16 testing). Revisa también `docs/BACKUPS.md`, `docs/DEPLOYMENT.md` y `docs/HANDOFF.md`.
2. Mira `git log --oneline`.
3. Resúmeme en 5 líneas dónde estamos, qué sigue y las reglas que no se pueden romper.

Contexto: fases 0–9 completas, 778 tests verdes, build sin warnings, `dotnet format` limpio.
El bot registra gastos, aprende keywords, tiene resumen/estadísticas/historial, aplica reglas
recurrentes con un scheduler, avisa al cruzar el presupuesto y dibuja gráficos de gasto.
`.env` local con bot de DEV en polling, base en `127.0.0.1:5435`; el VPS con webhook desplegado.

Haz los **resúmenes programados**: cuando en la fecha local del usuario sea el día 1, el bot
manda solo el cierre del mes anterior. Contenido del mensaje:
- Total gastado del mes cerrado, número de gastos y comparación con el mes anterior (con el
  aviso de que el periodo ya es completo).
- Las categorías con más gasto, con barras de texto; si el mes se sobregiró, decirlo.
- Un botón para copiar el presupuesto del mes anterior al mes nuevo
  (`CopyPreviousMonthAsync`) y, si el usuario lo toca, la confirmación de que quedó copiado o
  de que no había nada que copiar. El cierre se manda una sola vez aunque la app se reinicie.

Foco de implementación:
- Reutiliza la infraestructura del `RecurringExpenseScheduler`: un `BackgroundService` en
  `MyBudget.Telegram` que crea un scope por pasada, solo se registra con token configurado y
  resuelve servicios scoped desde el scope.
- El "día 1" se decide con `IUserLocalDate` sobre la zona del usuario, nunca con UTC.
- Persiste un marcador operativo de "ya enviado este mes" (una tabla como `budget_alerts`,
  con su migración y su FK de ownership) y decide el envío con una escritura idempotente
  (`ON CONFLICT DO NOTHING` o el marcador reclamado con un `UPDATE` condicional). Reiniciar no
  duplica.
- El servicio de cierre vive en Application (`IReportService` / `IBudgetService` ya dan todo
  lo necesario); el scheduler solo orquesta y envía. Nada de lógica financiera en Telegram.
- Los callbacks del botón (copiar presupuesto / descartar) van por el mecanismo de callback
  global o por una conversación corta, con datos de callback cortos y sin re-lecturas.
- Texto nuevo en `Messages.resx` + `MessageKeys`.

Tests: el marcador envía una sola vez por usuario y mes (integración), el render del cierre
(snapshot verbatim), y que tocar "copiar presupuesto" copia el mes anterior. Nada de esperar
un día real: el scheduler expone un `RunOnceAsync` que el test llama con un reloj fijo.

Reglas que no se rompen: dinero `long` exacto; `ExpenseDate` es `DateOnly` en hora local;
ownership con `userId` primero y FKs compuestas; la historia no se borra (desactivar); nada de
IA para categorizar; texto de usuario sólo en `Messages.resx` + `MessageKeys`; los casos de
uso en Application; las conversaciones de Telegram sólo recogen input y renderizan.

Trabaja en pasos cortos: implementa una cosa, corre
`dotnet build MyBudget.sln && dotnet test MyBudget.sln && dotnet format MyBudget.sln --verify-no-changes`,
muéstrame el resultado y sigue. No acumules turnos larguísimos.
```

## Backlog with decisions already taken

When these are built, seed the default categories in this order (the user's choice) and use
them as examples when creating recurring rules:

1. Mensualidades
2. Ocio
3. Ropa
4. Otras
5. Regalos
6. Vivienda
7. Mercado
8. Deporte

- **Onboarding with seed data.** On first use, offer to create those categories and leave one
  example recurring rule ready (for instance "Mensualidades"). Skippable: it never forces the
  user.
- **Edit a recurring rule** (amount and day) instead of deleting and recreating it.
- **Export CSV** of a date range's expenses. Import waits for later because of data integrity.
