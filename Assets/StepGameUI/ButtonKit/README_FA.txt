STEPGAME UI - BUTTON KIT v2
===========================

این نسخه برای مشکل رفتار Prefab داخل Canvas/Panel اصلاح شده است.

اصلاحات:
- Root هر Prefab از نوع RectTransform است.
- Anchor Min = (0.5, 0.5)
- Anchor Max = (0.5, 0.5)
- Pivot = (0.5, 0.5)
- Anchored Position = (0, 0)
- Local Scale = (1, 1, 1)
- Local Rotation = Identity
- Local Position Z = 0
- Layer = UI
- اندازه اولیه = 360 x 96

Prefabs:
Assets/StepGameUI/ButtonKit/Prefabs/
- StepGame_WhiteButton.prefab
- StepGame_PrimaryBlueButton.prefab

روش پیشنهادی و مطمئن:
1) Panel موردنظر را در Hierarchy انتخاب کن.
2) برو:
   GameObject > StepGame UI > White Button
   یا
   GameObject > StepGame UI > Primary Blue Button

دکمه مستقیم به عنوان Child همان Panel ساخته می‌شود و RectTransform آن نرمال است.

Drag & Drop:
می‌توانی Prefab را مستقیم روی اسم Panel در Hierarchy هم Drag کنی.
بعد باید Component اول آن Rect Transform باشد، نه Transform.

اگر یک UI Object قبلی رفتار بد دارد:
Tools > StepGame UI > Fix Selected UI RectTransform

بازسازی Prefabها:
Tools > StepGame UI > Rebuild Button Kit

نکته:
اگر نسخه قبلی Prefabها در پروژه وجود دارد، بعد از Import این نسخه حتماً یک بار:
Tools > StepGame UI > Rebuild Button Kit
را اجرا کن تا Prefabهای قدیمی با نسخه v2 جایگزین شوند.
