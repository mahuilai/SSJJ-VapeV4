__declspec(noinline) NTSTATUS MouseEvent(PMap Map, PDriverControl Control) {
    if (!Map->System.MouseDevice) return STATUS_UNSUCCESSFUL;

    MOUSE_INPUT_DATA InputData = Control->InputData;
    ULONG64 MouseDevice = Map->System.MouseDevice;

    ULONG Consumed{};

    _disable();
    KIRQL kirql = RaiseIrql(DISPATCH_LEVEL);
    ((PVOID(*)(ULONG64, PMOUSE_INPUT_DATA, PMOUSE_INPUT_DATA, PULONG))Map->System.MouseClassServiceCallback)(MouseDevice, &InputData, &InputData + 1, &Consumed);
    LowerIrql(kirql);
    _enable();

    return STATUS_SUCCESS;
}