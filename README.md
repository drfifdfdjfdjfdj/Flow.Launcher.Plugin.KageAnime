# KageAnime — плагин для Flow Launcher

Поиск аниме прямо из Flow Launcher с открытием плеера на [anime.hitler.site](https://anime.hitler.site).

## Возможности

- `ka <название>` — поиск по русским и английским названиям (Kodik + Yummy + AniLibria)
  - постер, год, рейтинг, тип и источник в подсказке
  - **Enter открывает тайтл сразу в плеере** (deep-link webapp)
  - Shift+Enter — контекстное меню: страница поиска на сайте / прямая ссылка
- `ka ongoing` — последние онгоинги
- `ka random` — случайное аниме

## Установка

Скачайте [Flow.Launcher.Plugin.KageAnime.zip](../../releases/latest) со страницы
релизов, распакуйте в `%APPDATA%\FlowLauncher\Plugins\KageAnime` и перезапустите Flow.
В Plugin Store появится после апрува.

## Технически

- Только стандартная библиотека Python (никаких pip-зависимостей)
- Кеш поиска 5 минут, постеры кешируются локально
- Fuzzy-поиск с транслитерацией RU↔EN на стороне API

Лицензия: MIT
