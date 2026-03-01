# 📦 Bye-TCP Internet — Руководство по публикации

## ✅ Файлы для публикации

### 📁 Необходимые папки и файлы

```
bye-tcp-internet/
├── 📂 src/                          ✅ ИСХОДНЫЙ КОД
│   ├── ByeTcp.UI.Wpf/               ✅ GUI приложение
│   ├── ByeTcp.Service/              ✅ Служба Windows
│   ├── ByeTcp.Orchestration/        ✅ Оркестратор
│   ├── ByeTcp.Monitoring/           ✅ Мониторинг
│   ├── ByeTcp.Decision/             ✅ Rule Engine
│   ├── ByeTcp.Execution/            ✅ Применение настроек
│   ├── ByeTcp.Infrastructure/       ✅ Конфигурация
│   ├── ByeTcp.Contracts/            ✅ Модели данных
│   ├── ByeTcp.Client/               ✅ IPC клиент
│   └── ByeTcp.Core/                 ✅ Ядро (опционально)
│
├── 📂 publish/                      ✅ ГОТОВЫЕ БИНАРНИКИ
│   ├── UI/
│   │   └── ByeTcp.UI.Wpf.exe        ✅ GUI приложение (148 KB)
│   └── service/
│       └── ByeTcp.Service.exe       ✅ Служба (70 MB)
│
├── 📂 config/                       ✅ КОНФИГУРАЦИЯ
│   ├── profiles.json                ✅ 8 профилей
│   └── rules.json                   ✅ 22 правила
│
├── 📂 schemas/                      ✅ JSON SCHEMA
│   ├── profiles.schema.json
│   └── rules.schema.json
│
├── 📂 scripts/                      ✅ СКРИПТЫ
│   ├── apply.ps1                    ✅ Применение профилей
│   ├── demo.ps1                     ✅ Демо/мониторинг
│   ├── install.ps1                  ✅ Установка службы
│   └── build.ps1                    ✅ Сборка проекта
│
├── 📂 docs/                         ✅ ДОКУМЕНТАЦИЯ
│   ├── ARCHITECTURE_v2.md
│   ├── REFACTORING_SUMMARY.md
│   ├── IPC_INTEGRATION.md
│   └── CONVERTERS_GUIDE.md
│
├── README_PUBLISH.md                ✅ ГЛАВНАЯ ИНСТРУКЦИЯ
├── .gitignore                       ✅ ИГНОР ФАЙЛОВ
├── cleanup.ps1                      ✅ ОЧИСТКА
├── prepare-release.ps1              ✅ ПОДГОТОВКА РЕЛИЗА
├── ByeTcp.sln                       ✅ SOLUTION
└── LICENSE                          ✅ ЛИЦЕНЗИЯ
```

---

## ❌ Файлы для удаления (НЕ публиковать)

### Временные файлы сборки

```
❌ bin/          - Скомпилированные бинарники
❌ obj/          - Промежуточные файлы
❌ .vs/          - Настройки Visual Studio
❌ .git/         - Git репозиторий
```

### Личные данные и логи

```
❌ logs/         - Логи службы
❌ backups/      - Резервные копии
❌ state/        - Состояние приложения
❌ cache/        - Кэш
```

### Тесты (опционально)

```
❌ tests/        - Unit тесты (если не нужны пользователям)
```

---

## 🚀 Скрипт очистки

```powershell
# Удалить bin и obj
.\cleanup.ps1

# Удалить всё включая publish
.\cleanup.ps1 -All

# Показать что будет удалено (без удаления)
.\cleanup.ps1 -DryRun
```

---

## 📦 Скрипт подготовки релиза

```powershell
# Подготовить чистую копию
.\prepare-release.ps1 -OutputPath ".\release"

# Создать ZIP архив
.\prepare-release.ps1 -OutputPath ".\release" -CreateZip
```

---

## 📋 Минимальный набор для публикации

### Вариант 1: Только бинарники (для пользователей)

```
publish/
├── UI/
│   └── ByeTcp.UI.Wpf.exe
└── service/
    └── ByeTcp.Service.exe

config/
├── profiles.json
└── rules.json

schemas/
├── profiles.schema.json
└── rules.schema.json

scripts/
├── apply.ps1
└── demo.ps1

README_PUBLISH.md
LICENSE
```

**Размер:** ~75 MB

### Вариант 2: Полные исходники (для разработчиков)

```
Всё из "Файлы для публикации" +
tests/ (опционально)
```

**Размер:** ~5-10 MB (без bin/obj)

---

## 🔧 Требования для запуска

### Для готовых бинарников

| Компонент | Версия | Ссылка |
|-----------|--------|--------|
| Windows | 10/11 x64 | Встроено |
| .NET 8 Runtime | 8.0+ | https://dotnet.microsoft.com/download/dotnet/8.0 |

### Для сборки из исходников

| Компонент | Версия | Ссылка |
|-----------|--------|--------|
| .NET 8 SDK | 8.0+ | https://dotnet.microsoft.com/download/dotnet/8.0 |
| Visual Studio 2022 | 17.0+ | https://visualstudio.microsoft.com/ |
| Windows App SDK | 1.4+ | Через VS Installer |

---

## 📝 Инструкция по публикации на GitHub

### 1. Подготовка

```powershell
# Очистка
.\cleanup.ps1 -All

# Создание релизной папки
.\prepare-release.ps1 -OutputPath ".\release" -CreateZip
```

### 2. Создание релиза на GitHub

1. Перейти на https://github.com/YOUR_USERNAME/bye-tcp-internet/releases
2. Нажать "Draft a new release"
3. Загрузить ZIP архив из папки `release`
4. Добавить описание изменений
5. Нажать "Publish release"

### 3. Обновление README

```markdown
## Скачать

- [ByeTcp-Internet-v2.0.zip](https://github.com/.../releases/download/v2.0/ByeTcp-Internet-v2.0.zip) - Готовые бинарники
- [Source Code.zip](https://github.com/.../archive/refs/tags/v2.0.zip) - Исходный код
```

---

## 📊 Размер публикации

| Компонент | Размер |
|-----------|--------|
| Исходный код (без bin/obj) | ~3-5 MB |
| Бинарники (publish/) | ~75 MB |
| ZIP архив (сжатое) | ~25-30 MB |

---

## ✅ Чеклист перед публикацией

- [ ] Очистить bin/obj папки
- [ ] Проверить работу GUI приложения
- [ ] Проверить работу службы
- [ ] Обновить версию в README
- [ ] Обновить CHANGELOG
- [ ] Протестировать на чистой Windows
- [ ] Добавить LICENSE файл
- [ ] Проверить .gitignore

---

## 📞 Контакты для вопросов

- Email: your-email@example.com
- GitHub Issues: https://github.com/.../issues

---

**© 2026 Bye-TCP Internet Project**
