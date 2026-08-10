__declspec(noinline) NTSTATUS KernelDeleteFile(PMap Map, PDriverControl Control)
{
    IO_STATUS_BLOCK IoStatusBlock;
    HANDLE FileHandle;
    OBJECT_ATTRIBUTES ObjectAttributes;
    InitializeObjectAttributes(&ObjectAttributes, 
        (PUNICODE_STRING)Control->Buffer, 
        OBJ_KERNEL_HANDLE | OBJ_CASE_INSENSITIVE, 0, 0);
    
    NTSTATUS Status = ((decltype(IoCreateFileEx)*)Map->ImportTable.IoCreateFileEx)(
        &FileHandle,
        SYNCHRONIZE | DELETE,
        &ObjectAttributes,
        &IoStatusBlock,
        nullptr,
        FILE_ATTRIBUTE_NORMAL,
        FILE_SHARE_DELETE,
        FILE_OPEN,
        FILE_NON_DIRECTORY_FILE | FILE_SYNCHRONOUS_IO_NONALERT,
        nullptr,
        0,
        CreateFileTypeNone,
        nullptr,
        IO_NO_PARAMETER_CHECKING,
        nullptr
        );

    if (!NT_SUCCESS(Status)) return Status;

    PFILE_OBJECT FileObject;

    Status =
        ((decltype(ObReferenceObjectByHandleWithTag)*)Map->ImportTable.ObReferenceObjectByHandleWithTag)
        (FileHandle, SYNCHRONIZE | DELETE, *(POBJECT_TYPE*)Map->ImportTable.IoFileObjectType, KernelMode, 0, reinterpret_cast<PVOID*>(&FileObject), nullptr);
    
    if (!NT_SUCCESS(Status))
    {
        ((decltype(ObCloseHandle)*)Map->ImportTable.ObCloseHandle)(FileHandle, KernelMode);
        return Status;
    }

    PSECTION_OBJECT_POINTERS SectionObjectPointer = FileObject->SectionObjectPointer;
    SectionObjectPointer->ImageSectionObject = nullptr;

    BOOLEAN ImageSectionFlushed = 
        ((decltype(MmFlushImageSection)*)Map->ImportTable.MmFlushImageSection)
        (SectionObjectPointer, MmFlushForDelete);
    
    ((decltype(ObfDereferenceObject)*)Map->ImportTable.ObfDereferenceObject)(FileObject);
    ((decltype(ObCloseHandle)*)Map->ImportTable.ObCloseHandle)(FileHandle, KernelMode);

    if (ImageSectionFlushed)
    {
        Status = ((decltype(ZwDeleteFile)*)Map->ImportTable.ZwDeleteFile)(&ObjectAttributes);
        if (NT_SUCCESS(Status)) return Status;
    }

    return Status;
}