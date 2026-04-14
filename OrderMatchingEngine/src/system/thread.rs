pub fn set_core(core_id: usize) -> bool {
    #[cfg(target_os = "linux")]
    {
        unsafe {
            let mut set: libc::cpu_set_t = std::mem::zeroed();
            libc::CPU_SET(core_id, &mut set);
            let tid = libc::pthread_self();
            libc::pthread_setaffinity_np(tid, std::mem::size_of::<libc::cpu_set_t>(), &set) == 0
        }
    }

    #[cfg(target_os = "windows")]
    {
        use windows_sys::Win32::System::Threading::{GetCurrentThread, SetThreadAffinityMask};
        unsafe {
            let mask = 1 << core_id;
            let handle = GetCurrentThread();
            SetThreadAffinityMask(handle, mask) != 0
        }
    }

    #[cfg(target_os = "macos")]
    {
        unsafe {
            unsafe extern "C" {
                fn pthread_set_qos_class_self_np(
                    __qos_class: std::os::raw::c_uint,
                    __relative_priority: std::os::raw::c_int,
                ) -> std::os::raw::c_int;
            }

            let qos_result = pthread_set_qos_class_self_np(0x21, 0);

            if qos_result != 0 {
                eprintln!("[DEBUG] macOS QoS setting failed with code: {}", qos_result);
            }
            
            #[repr(C)]
            struct ThreadAffinityPolicy {
                affinity_tag: i32,
            }
            const THREAD_AFFINITY_POLICY: i32 = 4;

            let port = libc::pthread_mach_thread_np(libc::pthread_self());
            let mut policy = ThreadAffinityPolicy {
                affinity_tag: (core_id + 1) as i32,
            };

            let _ = libc::thread_policy_set(
                port,
                THREAD_AFFINITY_POLICY as u32,
                &mut policy as *mut _ as *mut i32,
                1,
            );

            qos_result == 0
        }
    }

    #[cfg(not(any(target_os = "linux", target_os = "windows", target_os = "macos")))]
    {
        let _ = core_id;
        false
    }
}
