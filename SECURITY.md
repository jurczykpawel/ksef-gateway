# Security policy / Polityka bezpieczeństwa

## Polski

**Zgłoszenia prywatne:** [GitHub Security Advisory](https://github.com/jurczykpawel/ksef-gateway/security/advisories/new), zgodnie z [security.txt](https://ksef.techskills.academy/.well-known/security.txt). Nie publikuj podatności, tokenów KSeF, certyfikatów, kluczy ani danych faktur w publicznych issues. Dołącz wersję/commit, konfigurację bez sekretów, kroki odtworzenia i przewidywany wpływ; używaj danych syntetycznych.

**Wsparcie:** poprawki bezpieczeństwa dotyczą wyłącznie najnowszej wydanej linii minor (np. po wydaniu 0.3.x linia 0.2.x nie jest wspierana). Instaluj najnowszy patch tej linii. Gałęzie rozwojowe i starsze wydania nie mają gwarantowanych poprawek.

**Czas odpowiedzi:** projekt utrzymywany społecznościowo; staramy się potwierdzić zgłoszenie w ciągu 7 dni roboczych. To cel, nie SLA. Termin poprawki i publikacji ustalamy ze zgłaszającym zależnie od wpływu; brak gwarantowanego terminu naprawy.

**Zakres:** kod gateway API, serwis PDF, obrazy, workflow i szablony wdrożeń w tym repozytorium. Podatności samego KSeF i bibliotek CIRFMF zgłaszaj również ich utrzymującym. Nie testuj cudzych instalacji ani produkcyjnego KSeF bez zgody właściciela. Błędy bez wpływu na bezpieczeństwo zgłaszaj jako zwykłe issues. Projekt nie prowadzi płatnego bug bounty.

## English

**Private reporting:** use a [GitHub Security Advisory](https://github.com/jurczykpawel/ksef-gateway/security/advisories/new), also listed in [security.txt](https://ksef.techskills.academy/.well-known/security.txt). Do not publish vulnerabilities, KSeF tokens, certificates, keys or invoice data in public issues. Include the version/commit, configuration without secrets, reproduction steps and expected impact; use synthetic data.

**Supported versions:** security fixes target only the latest released minor series (for example, once 0.3.x ships, 0.2.x is unsupported). Install its latest patch. Development branches and older releases have no guaranteed fixes.

**Response:** this is a community-maintained project. We aim to acknowledge reports within 7 business days; this is a target, not an SLA. Fix and disclosure timing is coordinated with the reporter according to impact, without a guaranteed resolution deadline.

**Scope:** gateway API, PDF service, images, workflows and deployment templates in this repository. Please also report vulnerabilities in KSeF itself or CIRFMF libraries to their maintainers. Do not test third-party installations or production KSeF without the owner's permission. Report bugs without security impact as regular issues. There is no paid bug bounty.
