EDYTOR KART GWENT
Podręcznik użytkownika

Interfejs edytora jest dostępny po polsku. Nazwy i opisy kart są odczytywane z
plików wybranego klienta GWENT; ich język zależy od dostępnych lokalizacji gry.

1. Uruchamianie i wybór klienta

Uruchom GwentCardEditor.exe. Edytor domyślnie otwiera oficjalną instalację GOG,
jeśli znajduje się ona w standardowej lokalizacji. Przycisk „Otwórz oficjalnego
klienta GOG” pozwala wrócić do niej, a „Wybierz folder klienta GWENT” umożliwia
wskazanie innej instalacji. Ostatnio wybrane źródło jest zapamiętywane. Pasek
źródła pokazuje ścieżkę i skrócony SHA-256 archiwum danych. Edycja i przycisk
„Zastosuj zmiany” zapisują zmiany wyłącznie w bieżącej sesji edytora; nie
instalują ich w grze.

Lista kart znajduje się po lewej stronie. Użyj zakładek frakcji, filtra kategorii
i pola wyszukiwania. Możesz szukać po nazwie lub ID. Karty, żetony i liderzy są
wyświetlani w osobnych kategoriach. Wybierz kartę, aby otworzyć jej ustawienia.

2. Edycja karty

Parametry
- Siła i pancerz: zmieniaj tylko wtedy, gdy pole jest aktywne.
- Koszt werbunku: koszt karty w talii; dla lidera jest to premia do prowizji talii.
- Rzadkość, frakcje i kategorie: wybierz jedną z dostępnych wartości. Główna i
  druga frakcja muszą być różne. Zmiana frakcji nie zmienia warunków ani celów
  zdolności.
- Statusy początkowe: zaznacz dostępne statusy. Ich nazwy są dodawane do opisu
  karty w języku klienta. Niedostępne statusy pozostają zablokowane; powód
  znajdziesz w podpowiedzi przy polu.

Zdolność
Sekcja zawiera sprawdzone wartości liczbowe istniejącej zdolności, np. obrażenia,
wzmocnienie, liczbę celów, ładunki, liczniki lub koszt. Etykieta i podpowiedź
wyjaśniają działanie pola. Jeśli liczba występuje również w opisie, edytor
aktualizuje tylko potwierdzone powiązane wpisy. Przed zatwierdzeniem sprawdź
listę zmian. Zmiany zdolności działają wyłącznie w trybie treningowym przeciwko
komputerowi; nie działają w meczach sieciowych.

Tekst karty
Każda zakładka odpowiada językowi dostępnym w plikach klienta. Możesz edytować
nazwę, opis zdolności i tekst fabularny. Brakujące wpisy nie są tworzone
automatycznie. Zachowaj znaczniki formatowania i zmienne. Aby wstawić nowy
wiersz, użyj zapisu „\n” zamiast klawisza Enter.

Wspólny szkic
Po edycji karty możesz od razu wybrać następną. Zmiany są automatycznie zbierane
w jednym szkicu projektu. „Zastosuj zmiany” otwiera listę pogrupowaną według kart.
Wybierz kartę, aby kontynuować jej edycję, albo usuń wszystkie jej zmiany.
„Cofnij” i „Ponów” działają na wspólnym szkicu.

3. Rada Balansu

Rada Balansu jest domyślnie włączona. Edytor korzysta z lokalnie zapisanego
zrzutu oficjalnego konstruktora talii playgwent.com. Naciśnij „Aktualizuj balans”,
aby pobrać najnowszy zrzut; aktualizuj go po każdej nowej decyzji Rady. Aktualizacja
nie odbywa się automatycznie. Przy włączonej opcji wyświetlana siła i koszt
werbunku zawierają korekty, które edytor uwzględnia przy eksporcie. Wyłącz Radę,
aby pracować wyłącznie na danych lokalnych klienta, jeśli nie zawierają poprawek.
Nie zmieniaj źródła danych w trakcie sesji.

4. Zastosowanie, cofanie i eksport

„Zastosuj zmiany” zatwierdza sprawdzony szkic w projekcie edytora, ale nie zapisuje
go w instalacji gry. „Cofnij” odwraca ostatnią operację, „Ponów” ją przywraca,
a „Resetuj wszystko” usuwa zmiany bieżącej sesji. Eksport tworzy pakiet ze zmianami.

Eksport tworzy nowy folder i nie nadpisuje wybranego źródła. Wskaż folder, który
jeszcze nie istnieje. W przypadku eksportu klienta instaluj w osobnej testowej
kopii GWENT wyłącznie archiwum data_definitions z katalogu głównego eksportu.
Folder _editor zawiera kopię zapasową i raport; nie kopiuj go do gry. Przed
zastąpieniem plików zamknij grę. Pakiety przywracaj w odwrotnej kolejności
instalacji.

5. Bezpieczne użycie

- Zachowaj oryginalnego klienta bez zmian; eksport sprawdzaj w osobnej instalacji.
- Nie usuwaj pakietów kopii zapasowych, dopóki może być potrzebne przywrócenie.
- Nie zmieniaj karty ani ustawienia, którego kontrolka jest zablokowana.
- Jeśli źródło zmieniło się poza bieżącą sesją, otwórz klienta ponownie i nie
  nadpisuj zmian zewnętrznych.
- Przed instalacją danych gry lub plików multimedialnych wykonaj kopię zapasową
  instalacji testowej.
- Kontrola struktury danych nie potwierdza balansu ani wszystkich interakcji.
  Zachowanie zmian sprawdź w grze.
