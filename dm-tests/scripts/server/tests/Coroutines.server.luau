function Entry()
	local c1 = false
	local c2 = false
	local c3 = false
	local c4 = false

	local co = coroutine.create(function()
		c2 = true
		coroutine.yield()
		c4 = true
	end)

	c1 = true
	coroutine.resume(co)
	c3 = true
	coroutine.resume(co)

	assert(c1, "c1 is false")
	assert(c2, "c2 is false")
	assert(c3, "c3 is false")
	assert(c4, "c4 is false")
end
