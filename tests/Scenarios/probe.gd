extends MainLoop
func _initialize():
    print("COMBOHINT_PROBE_USERDIR=" + OS.get_user_data_dir())
func _process(_delta):
    return true
