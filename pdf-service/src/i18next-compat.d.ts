// i18next v26 removed the `showSupportNotice` init option (the notice itself is gone),
// but the vendored lib (pdf-service/lib, CIRFMF/ksef-pdf-generator) still passes it.
// Re-declare it so the lib typechecks; at runtime v26 ignores unknown options.
import 'i18next';

declare module 'i18next' {
  interface InitOptions {
    showSupportNotice?: boolean;
  }
}
