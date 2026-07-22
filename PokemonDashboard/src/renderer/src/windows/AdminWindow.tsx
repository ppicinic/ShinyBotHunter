import React, { useEffect } from 'react'
import { AdminCard } from '../components/AdminCard'

export function AdminWindow(): JSX.Element {
  useEffect(() => {
    document.title = 'Pokemon Dashboard — Admin'
  }, [])

  return (
    <div className="h-screen">
      <AdminCard />
    </div>
  )
}
