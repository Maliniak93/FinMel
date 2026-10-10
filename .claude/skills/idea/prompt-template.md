<!-- Two templates: DESIGN (target design) and FIX (target fix). Copy one, from its frontmatter down to its last heading. Remove every HTML comment when writing the file; delete an optional section you leave empty. Polish text, code identifiers in English, no code fences, ≤ ~60 lines. -->

<!-- ===== DESIGN ===== -->
---
slug: <kebab-case>
title: <tytuł po polsku>
target: design
kind: <new|change|cleanup>
status: draft
created: <YYYY-MM-DD>
issue:
---
# <tytuł po polsku>

## Cel
<!-- 1–3 zdania: jaki problem użytkownika to rozwiązuje i co istnieje, gdy zmiana jest gotowa. -->

## Stan obecny
<!-- Tylko kind: change (inaczej usuń sekcję): jak to działa dziś z perspektywy użytkownika — ekran/route, co widać, co przeszkadza. -->

## Jak ma działać
<!-- Scenariusze z perspektywy użytkownika, krok po kroku, z konkretnymi przykładami: kwoty w PLN, daty, przed → po. Ekrany i route'y po nazwie. -->

## Decyzje już podjęte
<!-- Ustalone z użytkownikiem, /design ich nie pyta ponownie. Każda linia oznaczona **wymaganie** albo **sugestia**; MVP vs później też tutaj. -->

## Poza zakresem
<!-- Czego ta zmiana świadomie nie obejmuje — zwłaszcza sąsiednia rzecz, którą łatwo założyć. -->

## Ryzyka i uwagi
<!-- Opcjonalne: kolizje z ADR / hard rules / product "Out of scope", przypadki brzegowe, ryzyka danych. -->

## Otwarte pytania dla /design
<!-- Co /idea celowo zostawił dla /design: wybory techniczne (serwis, event, endpoint, model danych), fakty z kodu do sprawdzenia przez Explore. -->

## Powiązane
<!-- Opcjonalne: issues (#n — dlaczego), linie z ideas.md, ADR, zapisane prompty. Poprawka otwartego #n — tu pierwsza linia. -->

<!-- ===== FIX ===== -->
---
slug: <kebab-case>
title: <tytuł po polsku>
target: fix
kind: fix
status: draft
created: <YYYY-MM-DD>
issue:
---
# <tytuł po polsku>

## Objaw
<!-- Co jest zepsute, jednym-dwoma zdaniami, słowami użytkownika; URL ekranu, jeśli dotyczy. -->

## Kroki reprodukcji
<!-- Ponumerowane kroki z konkretnymi danymi (konto, aktywo, kwoty, daty), tak by /fix mógł je powtórzyć. -->

## Oczekiwane vs faktyczne
<!-- Dwie linie: Oczekiwane: … / Faktyczne: … — z wartościami. -->

## Od kiedy
<!-- Opcjonalne: od kiedy występuje, po jakiej zmianie/merge'u, czy zawsze czy czasem. -->

## Hipotezy
<!-- Opcjonalne: przypuszczenia co do przyczyny, oznaczone jako niezweryfikowane — /fix je sprawdza, nie przyjmuje. -->

## Poza zakresem
<!-- Czego naprawa nie obejmuje — sąsiednie usprawnienia i zmiany zachowania (te idą do /design). -->

## Powiązane
<!-- Opcjonalne: issues (#n — dlaczego), zapisane prompty. -->
